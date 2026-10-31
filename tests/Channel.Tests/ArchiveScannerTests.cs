using System.Net;
using System.Text;
using Channel.Core;
using Channel.Infrastructure;

namespace Channel.Tests;

public class ArchiveScannerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DotNetArchiveTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task PeriodUsesInclusiveStartExclusiveEndAndPagesBackwards()
    {
        var handler = new Pages([
            Page(102, Post(104, "2026-11-01T00:00:00Z") + Post(103, "2026-10-31T12:00:00Z") + Post(102, "2026-10-30T00:00:00Z")),
            Page(null, Post(101, "2026-10-29T12:00:00Z"))]);
        using var http = new HttpClient(handler);
        var result = await new ArchiveScanner(new TelegramSource(http, "donor")).Scan(new(DateTimeOffset.Parse("2026-10-30T00:00:00Z"), DateTimeOffset.Parse("2026-11-01T00:00:00Z")), null, default);
        Assert.Equal(new long[] { 102, 103 }, result.Posts.Select(p => p.Id));
        Assert.Equal(2, result.Pages); Assert.Null(result.Warning);
        Assert.Contains("before=102", handler.Urls[1]);
    }

    [Fact]
    public async Task LastCountSkipsMediaOnlyPagesAndDeduplicatesOverlappingResults()
    {
        var handler = new Pages([
            Page(104, Post(105, "2026-10-31T12:00:00Z") + Post(104, "2026-10-30T12:00:00Z")),
            Page(103, """<div data-post="donor/103"><time datetime="2026-10-29T12:00:00Z"></time></div>"""),
            Page(null, Post(104, "2026-10-30T12:00:00Z") + Post(102, "2026-10-28T12:00:00Z") + Post(101, "2026-10-27T12:00:00Z"))]);
        using var http = new HttpClient(handler);
        var result = await new ArchiveScanner(new TelegramSource(http, "donor")).Scan(new(LastPosts: 3), null, default);
        Assert.Equal(new long[] { 102, 104, 105 }, result.Posts.Select(p => p.Id));
        Assert.Equal(3, result.Pages);
    }

    [Fact]
    public async Task PageLimitIsReportedInsteadOfClaimingACompleteArchive()
    {
        using var http = new HttpClient(new Pages([Page(104, Post(105, "2026-10-31T12:00:00Z"))]));
        var result = await new ArchiveScanner(new TelegramSource(http, "donor")).Scan(new(LastPosts: 20, MaxPages: 1), null, default);
        Assert.NotNull(result.Warning); Assert.Single(result.Posts);
    }

    [Fact]
    public void EnqueueIsIdempotentForPublishedSkippedFailedAndPendingPosts()
    {
        var settings = new Settings { DataDirectory = directory };
        var store = new Store(directory);
        store.Add(new(1, "https://t.me/donor/1", "Статья", []), "Published");
        store.Add(new(2, "https://t.me/donor/2", "Реклама", []), "Skipped");
        store.Add(new(3, "https://t.me/donor/3", "Ошибка", []), "Failed");
        store.Set("cursor", "100");
        var posts = Enumerable.Range(1, 5).Select(i => new SourcePost(i, $"https://t.me/donor/{i}", "Текст", [])).ToArray();
        var first = ArchiveScanner.Enqueue(settings, posts); var second = ArchiveScanner.Enqueue(settings, posts);
        Assert.Equal(new ArchiveEnqueueResult(2, 3), first);
        Assert.Equal(new ArchiveEnqueueResult(0, 5), second);
        Assert.Equal("Published", store.Find(1)!.State); Assert.Equal("Skipped", store.Find(2)!.State); Assert.Equal("Failed", store.Find(3)!.State);
        Assert.Equal("Pending", store.Find(4)!.State); Assert.Equal("100", store.Get("cursor"));
    }

    [Fact]
    public void InvalidArchiveOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ArchiveRequest(LastPosts: 0).Validate());
        Assert.Throws<ArgumentException>(() => new ArchiveRequest().Validate());
    }

    private static string Post(long id, string date) => $"<div data-post=\"donor/{id}\"><div class=\"tgme_widget_message_text\">Статья {id}</div><time datetime=\"{date}\"></time></div>";
    private static string Page(long? next, string posts) => posts + (next is { } value ? $"<a data-before=\"{value}\"></a>" : "");
    private sealed class Pages(string[] pages) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = Urls.Count; Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pages[index], Encoding.UTF8, "text/html") });
        }
    }
}
