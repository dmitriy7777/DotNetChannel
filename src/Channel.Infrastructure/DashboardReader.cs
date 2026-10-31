using System.Text.Json;
using Channel.Core;
using Microsoft.Data.Sqlite;

namespace Channel.Infrastructure;

public record PostOverview(long Id, string State, string Status, string Title, Draft Draft, string? Error, string? PublicationUrl, PostTokenUsage? TokenUsage = null)
{ public string TokensDisplay => TokenUsage is { RecordedAttempts: > 0 } u ? u.TotalTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + (u.Attempts > u.RecordedAttempts ? "+" : "") : "—"; }
public record DashboardSnapshot(IReadOnlyList<PostOverview> Posts, Dictionary<string,string> Meta, int AttemptsToday, int Limit, string Source, string Target, string Model, bool AutoPublish, int PollSeconds, TokenUsageOverview? TokenUsage = null);

public static class DashboardReader
{
    public static DashboardSnapshot Read(Settings settings, DateTimeOffset now)
    {
        var posts = new List<PostOverview>(); var meta = new Dictionary<string,string>(); TokenUsageOverview? tokenUsage = null;
        var path = Path.Combine(settings.DataDirectory, "channel.db");
        if (File.Exists(path))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 2 }.ToString());
            connection.Open(); using var tx = connection.BeginTransaction(deferred: true);
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx; cmd.CommandText = "SELECT key,value FROM meta";
                using var reader = cmd.ExecuteReader(); while (reader.Read()) meta[reader.GetString(0)] = reader.GetString(1);
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx; cmd.CommandText = "SELECT id,state,payload,error FROM posts ORDER BY id DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt64(0); var state = reader.GetString(1);
                    var draft = JsonSerializer.Deserialize<Draft>(reader.GetString(2))!;
                    var titleText = string.IsNullOrWhiteSpace(draft.Text) ? draft.Source.Text : draft.Text;
                    var title = titleText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? $"Пост {id}";
                    var publishedUrl = meta.TryGetValue($"published:{id}", out var messageId) && settings.TargetChannel.StartsWith('@') ? $"https://t.me/{settings.TargetChannel.TrimStart('@')}/{messageId}" : null;
                    posts.Add(new(id, state, StatusLabel(state), title, draft, reader.IsDBNull(3) ? null : reader.GetString(3), publishedUrl));
                }
            }
            int.TryParse(meta.GetValueOrDefault("attempts:" + now.UtcDateTime.ToString("yyyy-MM-dd")), out var attemptsForUsage);
            tokenUsage = UsageLedger.Read(connection, tx, now, attemptsForUsage, meta.Where(p => p.Key.StartsWith("attempts:")).Sum(p => int.TryParse(p.Value, out var count) ? count : 0));
            for (var i = 0; i < posts.Count; i++) posts[i] = posts[i] with { TokenUsage = UsageLedger.ForPost(tokenUsage.Entries, posts[i].Id) };
            tx.Commit();
        }
        int.TryParse(meta.GetValueOrDefault("attempts:" + now.UtcDateTime.ToString("yyyy-MM-dd")), out var attempts);
        return new(posts, meta, attempts, settings.MaxDraftsPerDay, settings.SourceChannel, settings.TargetChannel, settings.Model, settings.AutoPublish, settings.PollSeconds, tokenUsage ?? new([], 0, 0, attempts));
    }

    public static string StatusLabel(string status) => status switch
    {
        "Pending" => "В очереди", "Processing" => "Обработка ИИ", "Ready" => "Готов к отправке", "Notifying" => "Отправка черновика",
        "Review" => "На согласовании", "Publishing" => "Публикуется", "Published" => "Опубликован", "Skipped" => "Пропущен",
        "Failed" => "Ошибка обработки", "Uncertain" => "Нужна проверка отправки", _ => status
    };
}
