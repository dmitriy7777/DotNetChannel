using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Channel.Core;

namespace Channel.Infrastructure;

public sealed class AiRewriter(HttpClient http, Settings settings)
{
    public async Task<Rewrite> Rewrite(SourcePost source, CancellationToken ct, Action<AiUsageReport>? onUsage = null)
    {
        if (source.Text.Length > 30000) throw new InvalidDataException("Исходный пост превышает 30000 символов; требуется ручной разбор.");
        var instructions = await File.ReadAllTextAsync(settings.PromptFile, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAiKey);
        request.Content = JsonContent.Create(new
        {
            model = settings.Model, store = false, instructions,
            input = JsonSerializer.Serialize(new { source.Text, source.Links }),
            max_output_tokens = 5000,
            text = new { format = new { type = "json_schema", name = "rewrite", strict = true, schema = new
            {
                type = "object", properties = new { relevant = new { type = "boolean" }, text = new { type = "string" }, reviewNotes = new { type = "string" } },
                required = new[] { "relevant", "text", "reviewNotes" }, additionalProperties = false
            } } }
        });
        using var response = await http.SendAsync(request, ct);
        long? remaining = response.Headers.TryGetValues("x-ratelimit-remaining-tokens", out var values) && long.TryParse(values.FirstOrDefault(), out var value) ? value : null;
        var reset = response.Headers.TryGetValues("x-ratelimit-reset-tokens", out var resets) ? resets.FirstOrDefault() : null;
        if (!response.IsSuccessStatusCode) onUsage?.Invoke(new(null, "HTTP " + (int)response.StatusCode, null, null, null, RateLimitRemainingTokens: remaining, RateLimitReset: reset));
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI HTTP {(int)response.StatusCode}. Проверьте модель, ключ, баланс и лимиты.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        long? Number(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var count) ? count : null;
        JsonElement Details(string key) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(key, out var d) ? d : default;
        onUsage?.Invoke(new(root.TryGetProperty("id", out var responseId) ? responseId.GetString() : null,
            root.TryGetProperty("status", out var status) ? status.GetString() ?? "Unknown" : "Unknown",
            Number(usage, "input_tokens"), Number(usage, "output_tokens"), Number(usage, "total_tokens"),
            Number(Details("input_tokens_details"), "cached_tokens"), Number(Details("output_tokens_details"), "reasoning_tokens"), remaining, reset));
        if (root.GetProperty("status").GetString() != "completed") throw new InvalidDataException("OpenAI не завершил генерацию; текст не отправлен.");
        var output = string.Concat(root.GetProperty("output").EnumerateArray()
            .Where(x => x.GetProperty("type").GetString() == "message")
            .SelectMany(x => x.GetProperty("content").EnumerateArray())
            .Where(x => x.GetProperty("type").GetString() == "output_text").Select(x => x.GetProperty("text").GetString()));
        var result = JsonSerializer.Deserialize<Rewrite>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Нет результата ИИ.");
        PostRules.Validate(result, source);
        return result;
    }
}
