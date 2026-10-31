using Channel.Infrastructure;

namespace Channel.Worker;

public static class Application
{
    public static async Task Run(string[] args, CancellationToken ct)
    {
        var rootArg = Array.IndexOf(args, "--root");
        var root = rootArg >= 0 ? Path.GetFullPath(args[rootArg + 1]) : LocateRoot();
        var settings = Settings.Load(root);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(150) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DotNetChannel/1.0");
        var source = new TelegramSource(http, settings.SourceChannel);
        if (args.Contains("--preview"))
        {
            var posts = await source.Read(ct);
            Console.WriteLine($"Получено постов: {posts.Count}\nПоследний: {posts[^1].Url}\n\n{posts[^1].Text}");
            return;
        }
        if (!args.Contains("--status")) settings.Validate(ai: !args.Contains("--setup"));
        var bot = new TelegramBot(http, settings.BotToken);
        if (args.Contains("--setup"))
        {
            await bot.Call("getMe", new { }, ct);
            Console.WriteLine("Напишите /start своему боту. Будут показаны ID и имя отправителя. Используйте только свой ID.");
            long offset = 0;
            while (!ct.IsCancellationRequested)
            {
                var updates = await bot.Updates(offset, ct);
                var found = false;
                foreach (var u in updates.EnumerateArray())
                {
                    offset = u.GetProperty("update_id").GetInt64() + 1;
                    if (u.TryGetProperty("message", out var m) && m.GetProperty("chat").GetProperty("type").GetString() == "private")
                    {
                        var from = m.GetProperty("from");
                        var username = from.TryGetProperty("username", out var name) ? name.GetString() : "без username";
                        Console.WriteLine($"Telegram ID: {from.GetProperty("id").GetInt64()}; username: {username}");
                        found = true;
                    }
                }
                if (found) return;
            }
            return;
        }
        Directory.CreateDirectory(settings.DataDirectory);
        using var processLock = new FileStream(Path.Combine(settings.DataDirectory, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var store = new Store(settings.DataDirectory);
        if (args.Contains("--status"))
        {
            foreach (var row in store.List()) Console.WriteLine($"{row.Id}: {row.State} (v{row.Draft.Revision}) {row.Error}");
            return;
        }
        store.Recover();
        var runner = new Runner(settings, store, source, new AiRewriter(http, settings), bot);
        await runner.Check(ct);
        if (args.Contains("--check")) { Console.WriteLine("Telegram: подключение, права публикации и отсутствие webhook проверены. ИИ проверяется при первом черновике."); return; }
        using var localShutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var monitor = RuntimeMonitor.Run(settings.DataDirectory, localShutdown);
        try { await runner.Run(args.Contains("--draft-latest"), localShutdown.Token); }
        catch (OperationCanceledException) when (localShutdown.IsCancellationRequested) { Console.WriteLine("Сервис остановлен по запросу."); }
        finally { localShutdown.Cancel(); await monitor; }
    }

    private static string LocateRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "settings.example.json"))) return d.FullName;
        throw new DirectoryNotFoundException("Не найден settings.example.json. Запускайте из папки проекта или укажите --root <папка>.");
    }
}
