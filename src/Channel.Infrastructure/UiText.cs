using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Channel.Infrastructure;

public static class UiText
{
    private static readonly IReadOnlyDictionary<string,string> English = Load();
    public static IEnumerable<string> Keys => English.Keys;
    public static string ResourceKey(string text) => "L" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    public static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language == "ru" ? "ru-RU" : "en-GB");
    public static string Get(string language, string text, params object?[] values)
    {
        var format = language == "ru" ? text : English.GetValueOrDefault(text, text);
        return values.Length == 0 ? format : string.Format(Culture(language), format, values);
    }
    public static string Error(string language, string text)
    {
        if (language == "ru") return text;
        if (English.TryGetValue(text, out var exact)) return exact;
        foreach (var item in English.Where(p => !p.Key.Contains('{')).OrderByDescending(p => p.Key.Length)) text = text.Replace(item.Key, item.Value, StringComparison.Ordinal);
        return text;
    }
    private static IReadOnlyDictionary<string,string> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("Channel.Infrastructure.Localization.en.json") ?? throw new InvalidOperationException("English UI catalog missing.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
}
