using System.Text.Json;
using Channel.Core;
using Microsoft.Data.Sqlite;

namespace Channel.Infrastructure;

public static class UsageLedger
{
    public const string Schema = "CREATE TABLE IF NOT EXISTS ai_usage(id TEXT PRIMARY KEY,post_id INTEGER NOT NULL,revision INTEGER NOT NULL,model TEXT NOT NULL,created_utc TEXT NOT NULL,report TEXT NOT NULL);";

    public static string Begin(string directory, long postId, int revision, string model)
    {
        using var c = Open(directory); using var command = c.CreateCommand(); var id = Guid.NewGuid().ToString("N");
        command.CommandText = Schema + "INSERT INTO ai_usage VALUES($id,$post,$revision,$model,$created,$report)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$post", postId); command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$model", model); command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(new AiUsageReport(null, "AwaitingUsage", null, null, null)));
        command.ExecuteNonQuery(); return id;
    }

    public static void Complete(string directory, string id, AiUsageReport report)
    {
        using var c = Open(directory); using var command = c.CreateCommand();
        command.CommandText = "UPDATE ai_usage SET report=$report WHERE id=$id";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(report)); command.ExecuteNonQuery();
    }

    public static TokenUsageOverview Read(SqliteConnection c, SqliteTransaction tx, DateTimeOffset now, int attemptsToday, int? attemptsAllTime = null)
    {
        var entries = new List<AiUsageEntry>();
        using (var exists = c.CreateCommand())
        {
            exists.Transaction = tx; exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='ai_usage'";
            if ((long)exists.ExecuteScalar()! == 0) return new(entries, 0, 0, attemptsToday, attemptsAllTime ?? attemptsToday);
        }
        using (var command = c.CreateCommand())
        {
            command.Transaction = tx; command.CommandText = "SELECT id,post_id,revision,model,created_utc,report FROM ai_usage ORDER BY created_utc DESC";
            using var r = command.ExecuteReader();
            while (r.Read()) entries.Add(new(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), DateTimeOffset.Parse(r.GetString(4), System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Deserialize<AiUsageReport>(r.GetString(5))!));
        }
        var today = entries.Where(e => e.CreatedUtc.UtcDateTime.Date == now.UtcDateTime.Date).ToArray();
        var missing = Math.Max(0, attemptsToday - today.Length) + today.Count(e => e.Report.TotalTokens is null);
        var missingAll = Math.Max(0, (attemptsAllTime ?? entries.Count) - entries.Count) + entries.Count(e => e.Report.TotalTokens is null);
        return new(entries, today.Sum(e => e.Report.TotalTokens ?? 0), entries.Sum(e => e.Report.TotalTokens ?? 0), missing, missingAll);
    }

    public static PostTokenUsage ForPost(IEnumerable<AiUsageEntry> entries, long postId)
    {
        var rows = entries.Where(e => e.PostId == postId).ToArray();
        return new(rows.Length, rows.Count(r => r.Report.TotalTokens is not null), rows.Sum(r => r.Report.InputTokens ?? 0), rows.Sum(r => r.Report.OutputTokens ?? 0), rows.Sum(r => r.Report.TotalTokens ?? 0), rows.Sum(r => r.Report.CachedInputTokens ?? 0), rows.Sum(r => r.Report.ReasoningTokens ?? 0));
    }

    private static SqliteConnection Open(string directory)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "channel.db"), DefaultTimeout = 5 }.ToString()); c.Open(); return c;
    }
}
