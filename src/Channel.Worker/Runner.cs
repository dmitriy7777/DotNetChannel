using System.Text.Json;
using Channel.Core;
using Channel.Infrastructure;

namespace Channel.Worker;

public sealed class Runner(Settings settings, Store store, TelegramSource source, AiRewriter ai, TelegramBot bot)
{
    public async Task Check(CancellationToken ct)
    {
        var webhook = await bot.Call("getWebhookInfo", new { }, ct);
        if (!string.IsNullOrEmpty(webhook.GetProperty("url").GetString())) throw new InvalidOperationException("У бота установлен webhook. Используйте отдельного нового бота для этого проекта.");
        var me = await bot.Call("getMe", new { }, ct);
        var member = await bot.Call("getChatMember", new { chat_id = settings.TargetChannel, user_id = me.GetProperty("id").GetInt64() }, ct);
        if (member.GetProperty("status").GetString() != "administrator" || !member.TryGetProperty("can_post_messages", out var canPost) || !canPost.GetBoolean())
            throw new InvalidOperationException("Боту нужны права администратора с разрешением публикации в вашем канале.");
        var admin = await bot.Call("getChat", new { chat_id = settings.AdminUserId }, ct);
        if (admin.GetProperty("type").GetString() != "private") throw new InvalidOperationException("AdminUserId должен указывать ваш личный чат с ботом.");
    }

    public async Task Run(bool latest, CancellationToken ct)
    {
        if (latest)
        {
            var posts = await source.Read(ct);
            store.Add(posts[^1]);
            if (store.Get("cursor") is null) store.Set("cursor", posts[^1].Id.ToString());
            Console.WriteLine($"Последний пост добавлен в очередь: {posts[^1].Url}. Уже обработанные посты не дублируются.");
        }
        Console.WriteLine("Сервис работает. Остановка: Ctrl+C. Управление: /status и кнопки в личном чате с ботом.");
        var nextPoll = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextPoll)
                {
                    nextPoll = DateTimeOffset.UtcNow.AddSeconds(settings.PollSeconds);
                    try { await Ingest(ct); store.Set("sourcePollUtc", DateTimeOffset.UtcNow.ToString("O")); store.Set("sourceError", ""); } catch (Exception e) when (!ct.IsCancellationRequested) { store.Set("sourceError", e.Message); Console.Error.WriteLine($"Получение источника: {e.Message}"); }
                    await ProcessOne(ct);
                }
                await NotifyOne(ct);
                var offset = long.Parse(store.Get("offset") ?? "0");
                var updates = await bot.Updates(offset, ct);
                foreach (var update in updates.EnumerateArray())
                {
                    store.Set("offset", (update.GetProperty("update_id").GetInt64() + 1).ToString());
                    try { await Handle(update, ct); }
                    catch (Exception e) when (!ct.IsCancellationRequested) { Console.Error.WriteLine($"Команда не завершена: {e.Message}"); }
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Цикл: {e.Message}. Повтор через 15 секунд.");
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }

    private async Task Ingest(CancellationToken ct)
    {
        var posts = await source.Read(ct);
        if (store.Get("cursor") is not { } saved)
        {
            store.Set("cursor", posts[^1].Id.ToString());
            Console.WriteLine($"Начальная отметка: {posts[^1].Id}. Старые посты пропущены. Для пробы используйте --draft-latest.");
            return;
        }
        var cursor = long.Parse(saved);
        var all = posts.ToList();
        for (var page = 0; all.Min(x => x.Id) > cursor && page < 20; page++)
        {
            var oldest = all.Min(x => x.Id);
            var previous = await source.Read(ct, oldest);
            if (previous.Min(x => x.Id) >= oldest) throw new InvalidDataException("Не удалось получить предыдущую страницу. Отметка чтения сохранена; нужен ручной разбор.");
            all.AddRange(previous);
        }
        if (all.Min(x => x.Id) > cursor) throw new InvalidDataException("Пропуск больше 20 страниц. Отметка не изменена; требуется ручной разбор.");
        foreach (var p in all.Where(x => x.Id > cursor).DistinctBy(x => x.Id).OrderBy(x => x.Id)) store.Add(p);
        store.Set("cursor", Math.Max(cursor, all.Max(x => x.Id)).ToString());
    }

    internal async Task ProcessOne(CancellationToken ct)
    {
        var budgetKey = "attempts:" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var count = int.Parse(store.Get(budgetKey) ?? "0");
        if (count >= settings.MaxDraftsPerDay) return;
        var row = store.List().FirstOrDefault(x => x.State == "Pending");
        if (row is null || !store.Claim(row.Id, "Pending", "Processing", row.Draft.Revision)) return;
        store.Set(budgetKey, (count + 1).ToString());
        try
        {
            var rewrite = await ai.Rewrite(row.Draft.Source, ct);
            var draft = row.Draft with { Text = rewrite.Text, Notes = rewrite.ReviewNotes };
            store.Save(row.Id, rewrite.Relevant ? "Ready" : "Skipped", draft);
            Console.WriteLine($"Пост {row.Id}: {(rewrite.Relevant ? "черновик готов" : "не по теме")}.");
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            store.Save(row.Id, "Failed", row.Draft, e.Message);
            await bot.Send(settings.AdminUserId, $"Не удалось обработать пост {row.Id}: {e.Message}\nПовтор: /retry {row.Id}", ct);
        }
    }

    internal async Task NotifyOne(CancellationToken ct)
    {
        var row = store.List().FirstOrDefault(x => x.State == "Ready");
        if (row is null) return;
        if (settings.AutoPublish)
        {
            if (!store.Claim(row.Id, "Ready", "Publishing", row.Draft.Revision)) return;
            try
            {
                var sent = await bot.Send(settings.TargetChannel, PostRules.Publication(row.Draft), ct);
                store.Save(row.Id, "Published", row.Draft);
                store.Set($"published:{row.Id}", sent.GetProperty("message_id").GetInt64().ToString());
                Console.WriteLine($"Пост {row.Id} опубликован автоматически.");
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                store.Save(row.Id, "Uncertain", row.Draft, "Автопубликация: " + e.Message);
                await bot.Send(settings.AdminUserId, $"Пост {row.Id}: результат отправки неизвестен. Проверьте канал, затем /resolve {row.Id} published или /resolve {row.Id} retry. При включённом AutoPublish retry повторяет отправку автоматически.", ct);
            }
            return;
        }
        if (!store.Claim(row.Id, "Ready", "Notifying", row.Draft.Revision)) return;
        try
        {
            var notes = string.IsNullOrWhiteSpace(row.Draft.Notes) ? "Проверьте техническую точность и код." : row.Draft.Notes;
            await bot.Send(settings.AdminUserId, $"Черновик {row.Id}, версия {row.Draft.Revision}.\nПроверка: {notes[..Math.Min(notes.Length, 1800)]}", ct);
            object Button(string title, string action) => new { text = title, callback_data = $"{action}:{row.Id}:{row.Draft.Revision}" };
            await bot.Send(settings.AdminUserId, PostRules.Publication(row.Draft), ct,
                new { inline_keyboard = new[] { new[] { Button("Опубликовать", "publish"), Button("Переработать", "rewrite"), Button("Пропустить", "skip") } } });
            store.Save(row.Id, "Review", row.Draft);
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { store.Save(row.Id, "Uncertain", row.Draft, "Отправка черновика: " + e.Message); }
    }

    public async Task Handle(JsonElement update, CancellationToken ct)
    {
        if (update.TryGetProperty("callback_query", out var callback))
        {
            if (callback.GetProperty("from").GetProperty("id").GetInt64() != settings.AdminUserId) return;
            if (!callback.TryGetProperty("message", out var message) || message.GetProperty("chat").GetProperty("id").GetInt64() != settings.AdminUserId) return;
            var id = callback.GetProperty("id").GetString()!;
            var parts = callback.GetProperty("data").GetString()!.Split(':');
            if (parts.Length != 3 || !long.TryParse(parts[1], out var postId) || !int.TryParse(parts[2], out var revision)) return;
            var row = store.Find(postId);
            if (row is null || row.State != "Review" || row.Draft.Revision != revision) { await bot.Answer(id, "Черновик уже обработан или эта версия устарела.", ct); return; }
            if (parts[0] == "publish")
            {
                if (!store.Claim(postId, "Review", "Publishing", revision)) return;
                try
                {
                    var sent = await bot.Send(settings.TargetChannel, PostRules.Publication(row.Draft), ct);
                    store.Save(postId, "Published", row.Draft);
                    store.Set($"published:{postId}", sent.GetProperty("message_id").GetInt64().ToString());
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    store.Save(postId, "Uncertain", row.Draft, "Публикация: " + e.Message);
                    await bot.Send(settings.AdminUserId, $"Пост {postId}: результат публикации неизвестен. Проверьте свой канал. Затем /resolve {postId} published, если пост есть, или /resolve {postId} retry, если его нет.", ct);
                    return;
                }
                await bot.Answer(id, "Опубликовано.", ct);
            }
            else if (parts[0] == "rewrite" && store.Claim(postId, "Review", "Processing", revision))
            {
                store.Save(postId, "Pending", row.Draft with { Revision = revision + 1, Text = "", Notes = "" });
                await bot.Answer(id, "Поставлено в очередь переработки.", ct);
            }
            else if (parts[0] == "skip" && store.Claim(postId, "Review", "Skipped", revision)) await bot.Answer(id, "Пропущено.", ct);
            return;
        }
        if (!update.TryGetProperty("message", out var m) || !m.TryGetProperty("from", out var from) || from.GetProperty("id").GetInt64() != settings.AdminUserId || m.GetProperty("chat").GetProperty("id").GetInt64() != settings.AdminUserId) return;
        var command = (m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (command.Length == 0) return;
        if (command[0] == "/status")
        {
            var counts = string.Join("\n", store.List().GroupBy(x => x.State).Select(g => $"{g.Key}: {g.Count()}"));
            var issues = string.Join("\n", store.List().Where(x => x.State is "Failed" or "Uncertain").TakeLast(10).Select(x => $"{x.Id}: {x.State}; {x.Error}"));
            await bot.Send(settings.AdminUserId, $"Очередь:\n{counts}\n\n{issues}", ct);
        }
        else if (command.Length >= 2 && long.TryParse(command[1], out var postId) && store.Find(postId) is { } row)
        {
            if (command[0] == "/retry" && row.State == "Failed")
            {
                store.Save(postId, "Pending", row.Draft with { Revision = row.Draft.Revision + 1 });
                await bot.Send(settings.AdminUserId, $"Пост {postId} поставлен в очередь. Учитывается дневной лимит.", ct);
            }
            else if (command[0] == "/resolve" && command.Length == 3 && row.State == "Uncertain" && command[2] is "published" or "retry")
            {
                var state = command[2] == "published" ? "Published" : string.IsNullOrWhiteSpace(row.Draft.Text) ? "Pending" : "Ready";
                store.Save(postId, state, row.Draft with { Revision = row.Draft.Revision + 1 });
                await bot.Send(settings.AdminUserId, $"Пост {postId}: {state}. Дальнейшая отправка зависит от настройки AutoPublish.", ct);
            }
            else await Help(ct);
        }
        else await Help(ct);
    }

    private Task<JsonElement> Help(CancellationToken ct) => bot.Send(settings.AdminUserId,
        "Команды:\n/status — очередь и ошибки\n/retry ID — повторить неудачную обработку\n/resolve ID published — подтвердить, что пост уже опубликован\n/resolve ID retry — после ручной проверки вернуть неопределённый пост на согласование\nПубликация — только кнопкой под актуальным черновиком.", ct);
}
