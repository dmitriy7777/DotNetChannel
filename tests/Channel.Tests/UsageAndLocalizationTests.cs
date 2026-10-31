using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Channel.Core;
using Channel.Infrastructure;

namespace Channel.Tests;

public class UsageAndLocalizationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ChannelUsageTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task CapturesActualUsageAndRateWindowWithoutDoubleCountingDetails()
    {
        var response = """{"id":"response-test","status":"completed","usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120,"input_tokens_details":{"cached_tokens":25},"output_tokens_details":{"reasoning_tokens":5}},"output":[{"type":"message","content":[{"type":"output_text","text":"{\"relevant\":true,\"text\":\"Useful explanation\",\"reviewNotes\":\"\"}"}]}]}""";
        var report = await Call(response, false);
        Assert.Equal(100, report.InputTokens); Assert.Equal(20, report.OutputTokens); Assert.Equal(120, report.TotalTokens);
        Assert.Equal(25, report.CachedInputTokens); Assert.Equal(5, report.ReasoningTokens); Assert.Equal(900, report.RateLimitRemainingTokens);
    }

    [Fact]
    public async Task CapturesUsageBeforeRejectingAnIncompleteGeneration()
    {
        var report = await Call("""{"status":"incomplete","usage":{"input_tokens":100,"output_tokens":40,"total_tokens":140},"output":[]}""", true);
        Assert.Equal(140, report.TotalTokens); Assert.Equal("incomplete", report.Status);
    }

    [Fact]
    public async Task MissingUsageIsUnknownRatherThanZero()
    {
        var report = await Call("""{"status":"incomplete","output":[]}""", true);
        Assert.Null(report.TotalTokens); Assert.Null(report.InputTokens);
    }

    [Fact]
    public void LedgerAggregatesRevisionsAndKeepsUnknownAttemptsVisible()
    {
        var settings = new Settings { DataDirectory = directory };
        var store = new Store(directory); store.Add(new(1, "https://t.me/donor/1", "Source", []));
        store.Set("attempts:" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), "5");
        var first = UsageLedger.Begin(directory, 1, 1, "model");
        var report = new AiUsageReport("response1", "completed", 100, 20, 120, 25, 5);
        UsageLedger.Complete(directory, first, report); UsageLedger.Complete(directory, first, report);
        var second = UsageLedger.Begin(directory, 1, 2, "model");
        UsageLedger.Complete(directory, second, new("response2", "incomplete", 50, 10, 60));
        UsageLedger.Begin(directory, 1, 3, "model");
        var snapshot = DashboardReader.Read(settings, DateTimeOffset.UtcNow);
        var total = snapshot.TokenUsage!;
        Assert.Equal(180, total.TodayTokens); Assert.Equal(180, total.AllTimeTokens);
        Assert.Equal(3, total.TodayUnrecordedAttempts);
        var article = Assert.Single(snapshot.Posts).TokenUsage!;
        Assert.Equal(3, article.Attempts); Assert.Equal(2, article.RecordedAttempts);
        Assert.Equal(150, article.InputTokens); Assert.Equal(30, article.OutputTokens); Assert.Equal(180, article.TotalTokens);
    }

    [Fact]
    public void HistoricalDatabaseDoesNotInventUsage()
    {
        var store = new Store(directory); store.Add(new(1, "https://t.me/donor/1", "Old article", []), "Published");
        store.Set("attempts:" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), "3");
        var snapshot = DashboardReader.Read(new Settings { DataDirectory = directory }, DateTimeOffset.UtcNow);
        Assert.Equal(3, snapshot.TokenUsage!.TodayUnrecordedAttempts);
        Assert.Empty(snapshot.TokenUsage.Entries); Assert.Equal("—", Assert.Single(snapshot.Posts).TokensDisplay);
    }

    [Fact]
    public void EnglishAndRussianResolveTheSameMessageAndArguments()
    {
        Assert.Equal("Worker running", UiText.Get("en", "Сервис работает"));
        Assert.Equal("Сервис работает", UiText.Get("ru", "Сервис работает"));
        Assert.Equal("Add to queue (3)", UiText.Get("en", "Добавить в очередь ({0})", 3));
        Assert.Equal("Добавить в очередь (3)", UiText.Get("ru", "Добавить в очередь ({0})", 3));
        Assert.Equal("Worker running", UiText.Get("unknown", "Сервис работает"));
    }

    [Fact]
    public void EnglishCatalogHasNoCyrillicAndPreservesFormatArguments()
    {
        foreach (var key in UiText.Keys)
        {
            var value = UiText.Get("en", key);
            Assert.False(Regex.IsMatch(value, "[А-Яа-яЁё]"), key);
            var pattern = @"\{(\d+)(?:[^}]*)\}";
            Assert.Equal(Regex.Matches(key, pattern).Select(m => m.Groups[1].Value).Order(), Regex.Matches(value, pattern).Select(m => m.Groups[1].Value).Order());
        }
        Assert.Equal(UiText.Keys.Count(), UiText.Keys.Select(UiText.ResourceKey).Distinct().Count());
    }

    private async Task<AiUsageReport> Call(string json, bool expectFailure)
    {
        Directory.CreateDirectory(directory); var prompt = Path.Combine(directory, "prompt.txt"); await File.WriteAllTextAsync(prompt, "Instructions");
        using var http = new HttpClient(new Stub(json));
        var ai = new AiRewriter(http, new Settings { PromptFile = prompt, Model = "test", OpenAiKey = "test" });
        AiUsageReport? report = null;
        var task = ai.Rewrite(new(1, "https://t.me/donor/1", "Technical article", []), default, value => report = value);
        if (expectFailure) await Assert.ThrowsAsync<InvalidDataException>(() => task); else await task;
        return Assert.IsType<AiUsageReport>(report);
    }
    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("x-ratelimit-remaining-tokens", "900"); response.Headers.Add("x-ratelimit-reset-tokens", "2s"); return Task.FromResult(response);
        }
    }
}
