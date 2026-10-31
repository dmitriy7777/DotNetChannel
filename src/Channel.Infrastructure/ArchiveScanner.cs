using Channel.Core;

namespace Channel.Infrastructure;

public record ArchiveRequest(DateTimeOffset? FromUtc = null, DateTimeOffset? BeforeUtc = null, int? LastPosts = null, int MaxPages = 50)
{
    public void Validate()
    {
        if (MaxPages is < 1 or > 100) throw new ArgumentException("Число страниц должно быть от 1 до 100.");
        if (LastPosts is { } count)
        {
            if (count is < 1 or > 1000 || FromUtc is not null || BeforeUtc is not null) throw new ArgumentException("Укажите от 1 до 1000 последних текстовых постов либо период.");
        }
        else if (FromUtc is null || BeforeUtc is null || FromUtc >= BeforeUtc) throw new ArgumentException("Выберите корректное начало и конец периода.");
    }
}

public record ArchiveProgress(int Pages, int Found);
public record ArchiveScanResult(IReadOnlyList<SourcePost> Posts, int Pages, int UndatedPosts, string? Warning);
public record ArchiveEnqueueResult(int Added, int AlreadyKnown);

public sealed class ArchiveScanner(TelegramSource source)
{
    public async Task<ArchiveScanResult> Scan(ArchiveRequest request, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        request.Validate(); var found = new Dictionary<long, SourcePost>(); var seen = new HashSet<long>();
        long? before = null; var undated = 0; var pages = 0; var complete = false; string? warning = null;
        while (pages < request.MaxPages)
        {
            ct.ThrowIfCancellationRequested();
            var page = await source.ReadPage(ct, before); pages++;
            foreach (var post in page.Posts.OrderByDescending(p => p.Id))
            {
                if (!seen.Add(post.Id)) continue;
                if (request.LastPosts is not null) found[post.Id] = post;
                else if (post.PublishedUtc is not { } date) undated++;
                else if (date >= request.FromUtc && date < request.BeforeUtc) found[post.Id] = post;
            }
            progress?.Report(new(pages, found.Count));
            if (request.LastPosts is { } count && found.Count >= count) { complete = true; break; }
            if (request.FromUtc is { } from && page.OldestDate is { } oldest && oldest < from) { complete = true; break; }
            if (page.NextBefore is null) { complete = true; break; }
            if (before is { } previous && page.NextBefore >= previous) { warning = "Telegram повторил страницу. Показана только прочитанная часть архива."; break; }
            before = page.NextBefore;
            await Task.Delay(150, ct);
        }
        if (!complete && warning is null) warning = $"Достигнут предел {request.MaxPages} страниц. Показана только прочитанная часть; сузьте период.";
        if (undated > 0) warning = (warning is null ? "" : warning + " ") + $"Постов без распознаваемой даты: {undated}; в выборку периода они не включены.";
        var posts = found.Values.OrderByDescending(p => p.Id).Take(request.LastPosts ?? int.MaxValue).OrderBy(p => p.Id).ToArray();
        return new(posts, pages, undated, warning);
    }

    public static ArchiveEnqueueResult Enqueue(Settings settings, IEnumerable<SourcePost> posts)
    {
        var store = new Store(settings.DataDirectory); var added = 0; var known = 0;
        foreach (var post in posts.DistinctBy(p => p.Id).OrderBy(p => p.Id))
        {
            if (store.Add(post)) added++; else known++;
        }
        return new(added, known);
    }
}
