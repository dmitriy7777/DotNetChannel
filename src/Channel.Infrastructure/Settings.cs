using System.Text.Json;
using System.Text.RegularExpressions;

namespace Channel.Infrastructure;

public sealed class Settings
{
    public string SourceChannel { get; set; } = "NetDeveloperDiary";
    public string BotToken { get; set; } = "";
    public long AdminUserId { get; set; }
    public string TargetChannel { get; set; } = "";
    public string OpenAiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public bool AutoPublish { get; set; } = false;
    public int PollSeconds { get; set; } = 120;
    public int MaxDraftsPerDay { get; set; } = 3;
    public string DataDirectory { get; set; } = "data";
    public string PromptFile { get; set; } = "prompts/rewrite.txt";

    public static Settings Load(string root)
    {
        var file = Path.Combine(root, "settings.local.json");
        var s = File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })! : new();
        s.BotToken = Environment.GetEnvironmentVariable("CHANNEL_BOT_TOKEN") ?? s.BotToken;
        s.OpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? s.OpenAiKey;
        s.Model = Environment.GetEnvironmentVariable("CHANNEL_AI_MODEL") ?? s.Model;
        if (!Regex.IsMatch(s.SourceChannel, "^[A-Za-z][A-Za-z0-9_]{4,31}$")) throw new InvalidDataException("Неверный SourceChannel.");
        if (s.PollSeconds < 30 || s.MaxDraftsPerDay is < 1 or > 100) throw new InvalidDataException("PollSeconds >= 30, MaxDraftsPerDay: 1..100.");
        s.DataDirectory = Path.GetFullPath(s.DataDirectory, root);
        s.PromptFile = Path.GetFullPath(s.PromptFile, root);
        return s;
    }

    public void Validate(bool ai = true)
    {
        if (string.IsNullOrWhiteSpace(BotToken)) throw new InvalidDataException("Заполните BotToken в settings.local.json.");
        if (!ai) return;
        if (AdminUserId <= 0 || string.IsNullOrWhiteSpace(TargetChannel) || string.IsNullOrWhiteSpace(OpenAiKey) || string.IsNullOrWhiteSpace(Model))
            throw new InvalidDataException("Заполните AdminUserId, TargetChannel, OpenAiKey и Model. См. README.md.");
        if (TargetChannel == SourceChannel || TargetChannel == "@" + SourceChannel) throw new InvalidDataException("Канал назначения совпадает с донором.");
    }
}
