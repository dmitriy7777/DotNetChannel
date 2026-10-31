using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Channel.Infrastructure;

public sealed class TelegramBot(HttpClient http, string token)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public async Task<JsonElement> Call(string method, object body, CancellationToken ct)
    {
        // Never log request URI: it contains the bot token.
        HttpResponseMessage response;
        try { response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/{method}", body, JsonOptions, ct); }
        catch (HttpRequestException) { throw new IOException("Соединение с Telegram прервано. Результат отправки может быть неизвестен."); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new IOException($"Telegram HTTP {(int)response.StatusCode}; проверьте настройки/права или повторите позже.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("Telegram отклонил запрос.");
            return json.RootElement.GetProperty("result").Clone();
        }
    }
    public Task<JsonElement> Send(object chat, string text, CancellationToken ct, object? keyboard = null) { var rendered = TelegramText.Render(text); return
        Call("sendMessage", new { chat_id = chat, text = rendered.Text, entities = rendered.Entities, reply_markup = keyboard, link_preview_options = new { is_disabled = true } }, ct); }
    public Task<JsonElement> Updates(long offset, CancellationToken ct) => Call("getUpdates", new { offset, timeout = 20, allowed_updates = new[] { "message", "callback_query" } }, ct);
    public Task<JsonElement> Answer(string id, string text, CancellationToken ct) => Call("answerCallbackQuery", new { callback_query_id = id, text }, ct);
}
