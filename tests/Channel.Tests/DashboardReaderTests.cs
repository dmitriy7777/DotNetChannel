using Channel.Infrastructure;

namespace Channel.Tests;

public class DashboardReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DotNetDashboardTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void DashboardDoesNotCreateAnAbsentDatabase()
    {
        var snapshot = DashboardReader.Read(new Settings { DataDirectory = directory }, DateTimeOffset.UtcNow);
        Assert.Empty(snapshot.Posts); Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ReadsUtcBudgetAndPublishedLinksWithoutModifyingQueue()
    {
        var store = new Store(directory);
        var source = new Channel.Core.SourcePost(10, "https://t.me/donor/10", "Оригинал", []);
        store.Add(source); store.Save(10, "Published", new(source, "Заголовок\nОбъяснение"));
        store.Set("published:10", "2"); store.Set("attempts:2026-10-31", "3"); store.Set("attempts:2026-11-01", "1");
        var snapshot = DashboardReader.Read(new Settings { DataDirectory = directory, TargetChannel = "@mine", MaxDraftsPerDay = 3 }, DateTimeOffset.Parse("2026-11-01T00:30:00+02:00"));
        Assert.Equal(3, snapshot.AttemptsToday);
        var row = Assert.Single(snapshot.Posts);
        Assert.Equal("https://t.me/mine/2", row.PublicationUrl); Assert.Equal("Заголовок", row.Title);
        Assert.Equal("Опубликован", row.Status); Assert.Equal("Published", store.Find(10)!.State);
    }
}
