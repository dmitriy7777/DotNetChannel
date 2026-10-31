using System.Net;
using System.Text;
using Channel.Core;
using Channel.Infrastructure;
using Channel.Worker;

namespace Channel.Tests;

public sealed class AutomaticModeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DotNetChannelTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task AutomaticModePublishesOnceWithoutApproval()
    {
        var handler = new Stub(); using var http = new HttpClient(handler);
        var settings = new Settings { AutoPublish = true, AdminUserId = 42, TargetChannel = "@mine" };
        var store = new Store(directory); var post = new SourcePost(1, "https://t.me/example/1", "C#", []);
        store.Add(post); store.Save(1, "Ready", new(post, "Простой текст"));
        var runner = new Runner(settings, store, new TelegramSource(http, "example"), new AiRewriter(http, settings), new TelegramBot(http, "dummy"));
        await runner.NotifyOne(default); await runner.NotifyOne(default);
        Assert.Equal(1, handler.Calls); Assert.Equal("Published", store.Find(1)!.State);
    }

    [Fact]
    public async Task DailyBudgetCountsFailedAiAttempts()
    {
        var handler = new Stub(); using var http = new HttpClient(handler);
        Directory.CreateDirectory(directory); var prompt = Path.Combine(directory, "prompt.txt"); await File.WriteAllTextAsync(prompt, "Правила");
        var settings = new Settings { AdminUserId = 42, MaxDraftsPerDay = 1, PromptFile = prompt, OpenAiKey = "test", Model = "test" };
        var store = new Store(directory);
        store.Add(new(1, "https://t.me/example/1", "C#", [])); store.Add(new(2, "https://t.me/example/2", "C#", []));
        var runner = new Runner(settings, store, new TelegramSource(http, "example"), new AiRewriter(http, settings), new TelegramBot(http, "dummy"));
        await runner.ProcessOne(default); await runner.ProcessOne(default);
        Assert.Equal(1, handler.AiCalls);
        Assert.Equal("Failed", store.Find(1)!.State); Assert.Equal("Pending", store.Find(2)!.State);
    }

    private sealed class Stub : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int AiCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.RequestUri!.Host == "api.openai.com") { AiCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"result":{"message_id":1}}""", Encoding.UTF8, "application/json") });
        }
    }
}
