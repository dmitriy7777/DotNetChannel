using System.ComponentModel;
using Channel.Core;
using Channel.Infrastructure;

namespace Channel.Admin;

public sealed class ArchiveRow(SourcePost source, string? knownStatus, TimeZoneInfo timezone, string language = "en") : INotifyPropertyChanged
{
    private bool selected = knownStatus is null;
    public SourcePost Source { get; } = source;
    public long Id => Source.Id;
    public bool CanQueue => knownStatus is null;
    public string Status => knownStatus is null ? UiText.Get(language, "Новый · отбор ИИ") : UiText.Get(language, "Уже учтён: {0}", UiText.Get(language, knownStatus));
    public string Date => Source.PublishedUtc is { } date ? TimeZoneInfo.ConvertTime(date, timezone).ToString("dd.MM.yyyy HH:mm") : UiText.Get(language, "Дата неизвестна");
    public string Title => Source.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault(x => !x.StartsWith("День ", StringComparison.OrdinalIgnoreCase)) ?? $"Пост {Id}";
    public bool IsSelected { get => selected; set { var next = value && CanQueue; if (selected == next) return; selected = next; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
