using System.Net;
using System.Text;
using System.Text.Json;
using Channel.Core;
using Channel.Infrastructure;
using Channel.Worker;

namespace Channel.Tests;

public sealed class PipelineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DotNetChannelTests", Guid.NewGuid().ToString("N"));
    private static SourcePost Post => new(123, "https://t.me/NetDeveloperDiary/123", "C# пример", []);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void ParserPreservesCodeLineBreaksAndDecodesEntities()
    {
        var posts = TelegramSource.Parse("""<div data-post="NetDeveloperDiary/123"><div class="tgme_widget_message_text js-message_text">Тема<br/><pre>if (a &lt; b)<br/>  Go();</pre><a href="https://example.org/article">Источник</a></div></div>""", "NetDeveloperDiary");
        var post = Assert.Single(posts);
        Assert.Contains("if (a < b)", post.Text); Assert.Contains("\n  Go();", post.Text.Replace("\r\n", "\n"));
        Assert.Contains("https://example.org/article", post.Links);
    }

    [Fact]
    public void StoreDeduplicatesAndClaimsOnlyMatchingRevision()
    {
        var store = new Store(directory); store.Add(Post); store.Add(Post);
        Assert.Single(store.List());
        Assert.False(store.Claim(123, "Pending", "Processing", 2));
        Assert.True(store.Claim(123, "Pending", "Processing", 1));
        Assert.False(store.Claim(123, "Pending", "Processing", 1));
    }

    [Theory]
    [InlineData("Publishing", "Uncertain")]
    [InlineData("Notifying", "Uncertain")]
    [InlineData("Processing", "Failed")]
    [InlineData("Published", "Published")]
    public void RestartNeverAutomaticallyRepeatsAnUncertainSend(string before, string after)
    {
        var store = new Store(directory); store.Add(Post, before); store.Recover();
        Assert.Equal(after, store.Find(123)!.State);
    }

    [Fact]
    public void CodeOffsetsUseUtf16AndDoNotInterpretHtml()
    {
        var rendered = TelegramText.Render("👋\n```csharp\nif (x < 2)\n```\nКонец");
        var entity = Assert.Single(rendered.Entities);
        Assert.Equal(3, entity.offset);
        Assert.Equal("if (x < 2)", rendered.Text.Substring(entity.offset, entity.length));
        Assert.DoesNotContain("```", rendered.Text);
    }

    [Fact]
    public void OversizedRewriteIsBlocked()
    {
        Assert.Throws<InvalidDataException>(() => PostRules.Validate(new(true, new string('x', 3901), ""), Post));
    }

    [Fact]
    public async Task ApprovalRequiresOwnerAndCannotPublishTwice()
    {
        var handler = new FakeHttp(); using var http = new HttpClient(handler);
        var settings = new Settings { AdminUserId = 42, TargetChannel = "@mine" };
        var store = new Store(directory); store.Add(Post); store.Save(123, "Review", new Draft(Post, "Понятное объяснение"));
        var runner = new Runner(settings, store, new TelegramSource(http, "NetDeveloperDiary"), new AiRewriter(http, settings), new TelegramBot(http, "dummy"));
        await runner.Handle(Callback(99, 1), default);
        Assert.Empty(handler.Requests);
        await runner.Handle(Callback(42, 2), default);
        Assert.Equal("Review", store.Find(123)!.State);
        await runner.Handle(Callback(42, 1), default);
        await runner.Handle(Callback(42, 1), default);
        Assert.Equal("Published", store.Find(123)!.State);
        Assert.Single(handler.Requests, x => x.Url.EndsWith("/sendMessage"));
    }

    [Fact]
    public async Task AmbiguousPublicationRequiresManualResolution()
    {
        var handler = new FakeHttp { FailPublication = true }; using var http = new HttpClient(handler);
        var settings = new Settings { AdminUserId = 42, TargetChannel = "@mine" };
        var store = new Store(directory); store.Add(Post); store.Save(123, "Review", new Draft(Post, "Текст"));
        var runner = new Runner(settings, store, new TelegramSource(http, "NetDeveloperDiary"), new AiRewriter(http, settings), new TelegramBot(http, "dummy"));
        await runner.Handle(Callback(42, 1), default);
        await runner.Handle(Callback(42, 1), default);
        Assert.Equal("Uncertain", store.Find(123)!.State);
        Assert.Single(handler.Requests, x => x.Body.Contains("@mine"));
    }

    [Fact]
    public async Task AiUsesStructuredOutputAndRejectsIncompleteResponse()
    {
        Directory.CreateDirectory(directory); var prompt = Path.Combine(directory, "prompt.txt"); await File.WriteAllTextAsync(prompt, "Правила");
        var handler = new FakeHttp { AiResponse = """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"{\"relevant\":true,\"text\":\"Объяснение\",\"reviewNotes\":\"Проверить код\"}"}]}]}""" };
        using var http = new HttpClient(handler);
        var ai = new AiRewriter(http, new Settings { OpenAiKey = "test", Model = "test-model", PromptFile = prompt });
        var result = await ai.Rewrite(Post, default);
        Assert.True(result.Relevant); Assert.Equal("Объяснение", result.Text);
        Assert.Contains("json_schema", handler.Requests[0].Body);
        handler.AiResponse = """{"status":"incomplete","output":[]}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => ai.Rewrite(Post, default));
    }

    private static JsonElement Callback(long user, int revision) => JsonSerializer.SerializeToElement(new
    {
        callback_query = new { id = "callback", from = new { id = user }, message = new { chat = new { id = user } }, data = $"publish:123:{revision}" }
    });

    private sealed class FakeHttp : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];
        public bool FailPublication { get; init; }
        public string AiResponse { get; set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var url = request.RequestUri!.ToString(); Requests.Add((url, body));
            if (FailPublication && body.Contains("@mine")) throw new HttpRequestException("Network failed");
            var json = url.Contains("api.openai.com") ? AiResponse : """{"ok":true,"result":{"message_id":7}}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
