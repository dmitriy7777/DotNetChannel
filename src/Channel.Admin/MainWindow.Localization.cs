using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Channel.Infrastructure;

namespace Channel.Admin;

public partial class MainWindow
{
    private string uiLanguage = "en";
    private string T(string text, params object?[] values) => UiText.Get(uiLanguage, text, values);
    private string ErrorText(string text) => UiText.Error(uiLanguage, text);
    private void LoadLanguagePreference()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Settings.Load(root).DataDirectory, "admin.preferences.json")));
            uiLanguage = json.RootElement.GetProperty("Language").GetString() == "ru" ? "ru" : "en";
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException) { uiLanguage = "en"; }
        var argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--lang="));
        if (argument is not null) uiLanguage = argument == "--lang=ru" ? "ru" : "en";
        ApplyLanguageResources();
    }
    private void ApplyLanguageResources()
    {
        foreach (var key in UiText.Keys) Application.Current.Resources[UiText.ResourceKey(key)] = T(key);
    }
    private async void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        uiLanguage = ((ComboBoxItem)LanguagePicker.SelectedItem).Tag as string ?? "en";
        ApplyLanguageResources(); Language = XmlLanguage.GetLanguage(UiText.Culture(uiLanguage).Name);
        var settings = Settings.Load(root); Directory.CreateDirectory(settings.DataDirectory);
        var file = Path.Combine(settings.DataDirectory, "admin.preferences.json");
        try { File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new { Language = uiLanguage })); File.Move(file + ".tmp", file, overwrite: true); }
        catch (IOException error) { Footer.Text = ErrorText(error.Message); }
        rowsKey = "";
        if (archiveRows.Count > 0)
        {
            var selected = archiveRows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();
            LoadArchiveRows(archiveRows.Select(r => r.Source).ToArray(), settings);
            foreach (var row in archiveRows) row.IsSelected = selected.Contains(row.Id);
        }
        SetControlTooltips(); UpdateArchiveSelection(); PostSelected(this, null!); await Refresh(); await LoadTelegram();
    }
    private void SetControlTooltips()
    {
        StartButton.ToolTip = T("Запустить сервис (F6)"); StopButton.ToolTip = T("Штатно остановить сервис (F7)"); RestartButton.ToolTip = T("Перезапустить сервис (F8)");
    }
}
