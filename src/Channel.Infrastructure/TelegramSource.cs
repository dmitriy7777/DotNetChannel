using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Channel.Core;
using HtmlAgilityPack;

namespace Channel.Infrastructure;

public record TelegramSourcePage(IReadOnlyList<SourcePost> Posts, long? NextBefore, DateTimeOffset? OldestDate);

public sealed class TelegramSource(HttpClient http, string channel)
{
    public async Task<IReadOnlyList<SourcePost>> Read(CancellationToken ct, long? before = null)
    {
        var page = await ReadPage(ct, before);
        if (page.Posts.Count == 0) throw new InvalidDataException("Публичная страница не содержит текстовых постов: возможно, изменена разметка или канал недоступен.");
        return page.Posts;
    }

    public async Task<TelegramSourcePage> ReadPage(CancellationToken ct, long? before = null)
    {
        var html = await http.GetStringAsync($"https://t.me/s/{channel}" + (before is null ? "" : $"?before={before}"), ct);
        return ParsePage(html, channel);
    }

    public static TelegramSourcePage ParsePage(string html, string channel)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var posts = Parse(html, channel);
        long? next = null;
        var link = doc.DocumentNode.SelectSingleNode("//a[@data-before]");
        if (link is not null && long.TryParse(link.GetAttributeValue("data-before", ""), out var id)) next = id;
        var dates = (doc.DocumentNode.SelectNodes("//*[@data-post]") ?? Enumerable.Empty<HtmlNode>())
            .Where(n => n.GetAttributeValue("data-post", "").StartsWith(channel + "/", StringComparison.OrdinalIgnoreCase))
            .Select(ReadDate).Where(d => d is not null).Select(d => d!.Value).ToArray();
        if (posts.Count == 0 && dates.Length == 0 && !html.Contains("tgme_channel_info", StringComparison.Ordinal))
            throw new InvalidDataException("Страница архива не распознана. Проверьте доступность канала и разметку Telegram.");
        return new(posts, next, dates.Length == 0 ? null : dates.Min());
    }

    public static IReadOnlyList<SourcePost> Parse(string html, string channel)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var result = new List<SourcePost>();
        foreach (var node in doc.DocumentNode.SelectNodes("//*[@data-post]") ?? Enumerable.Empty<HtmlNode>())
        {
            var key = node.GetAttributeValue("data-post", "").Split('/');
            if (key.Length != 2 || !key[0].Equals(channel, StringComparison.OrdinalIgnoreCase) || !long.TryParse(key[1], out var id)) continue;
            var body = node.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' tgme_widget_message_text ')]");
            if (body is null) continue;
            var sb = new StringBuilder(); AppendText(body, sb);
            var text = WebUtility.HtmlDecode(sb.ToString()).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var links = (body.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
                .Select(x => x.GetAttributeValue("href", ""))
                .Where(x => Uri.TryCreate(x, UriKind.Absolute, out var u) && u.Scheme is "https" or "http").Distinct().ToArray();
            result.Add(new(id, $"https://t.me/{channel}/{id}", text, links, ReadDate(node)));
        }
        return result.DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
    }

    private static DateTimeOffset? ReadDate(HtmlNode node)
    {
        var value = node.SelectSingleNode(".//time[@datetime]")?.GetAttributeValue("datetime", "");
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static void AppendText(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text) { sb.Append(node.InnerText); return; }
        if (node.Name == "br") { sb.AppendLine(); return; }
        if (node.Name == "pre") sb.Append("\n```csharp\n");
        foreach (var child in node.ChildNodes) AppendText(child, sb);
        if (node.Name == "pre") sb.Append("\n```\n");
        if (node.Name is "p" or "div") sb.AppendLine();
    }
}
