using System.Text.Json;
using Channel.Core;
using Microsoft.Data.Sqlite;

namespace Channel.Infrastructure;

public sealed class Store
{
    private readonly string connectionString;
    private readonly string dataDirectory;
    public Store(string directory)
    {
        dataDirectory = directory;
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "channel.db"), DefaultTimeout = 10 }.ToString();
        Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS posts(id INTEGER PRIMARY KEY, state TEXT NOT NULL, payload TEXT NOT NULL, error TEXT); CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);");
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    private int Execute(string sql, params (string, object?)[] args)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (k,v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }
    public string BeginUsage(long postId, int revision, string model) => UsageLedger.Begin(dataDirectory, postId, revision, model);
    public void CompleteUsage(string id, AiUsageReport report) => UsageLedger.Complete(dataDirectory, id, report);
    public string? Get(string key)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM meta WHERE key=$key"; cmd.Parameters.AddWithValue("$key", key); return cmd.ExecuteScalar() as string;
    }
    public void Set(string key, string value) => Execute("INSERT INTO meta VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    public bool Add(SourcePost source, string state = "Pending") => Execute("INSERT OR IGNORE INTO posts VALUES($id,$state,$data,NULL)", ("$id", source.Id), ("$state", state), ("$data", JsonSerializer.Serialize(new Draft(source)))) == 1;
    public StoredDraft? Find(long id) => List().FirstOrDefault(x => x.Id == id);
    public List<StoredDraft> List()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,state,payload,error FROM posts ORDER BY id";
        using var r = cmd.ExecuteReader(); var rows = new List<StoredDraft>();
        while(r.Read()) rows.Add(new(r.GetInt64(0), r.GetString(1), JsonSerializer.Deserialize<Draft>(r.GetString(2))!, r.IsDBNull(3) ? null : r.GetString(3)));
        return rows;
    }
    public void Save(long id, string state, Draft draft, string? error = null) => Execute("UPDATE posts SET state=$state,payload=$data,error=$err WHERE id=$id", ("$id", id), ("$state", state), ("$data", JsonSerializer.Serialize(draft)), ("$err", error));
    public bool Claim(long id, string from, string to, int revision) => Execute("UPDATE posts SET state=$to WHERE id=$id AND state=$from AND json_extract(payload,'$.Revision')=$rev", ("$id", id), ("$from", from), ("$to", to), ("$rev", revision)) == 1;
    public void Recover()
    {
        Execute("UPDATE posts SET state='Uncertain',error='Процесс остановлен во время отправки. Проверьте Telegram вручную.' WHERE state IN ('Publishing','Notifying')");
        Execute("UPDATE posts SET state='Failed',error='Обработка прервана. Можно повторить через /retry.' WHERE state='Processing'");
    }
}
