using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Channel.Infrastructure;

namespace Channel.Admin;

public partial class MainWindow : Window
{
    private readonly string root;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private DashboardSnapshot? snapshot;
    private ServiceController controller = null!;
    private bool refreshing, busy, loaded, telegramBusy;
    private string rowsKey = "";
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");

    public MainWindow()
    {
        root = LocateRoot(); InitializeComponent();
        AddShortcut(Key.F5, RefreshClick); AddShortcut(Key.F6, StartClick); AddShortcut(Key.F7, StopClick); AddShortcut(Key.F8, RestartClick);
        StartButton.ToolTip = "Запустить сервис (F6)"; StopButton.ToolTip = "Штатно остановить сервис (F7)"; RestartButton.ToolTip = "Перезапустить сервис (F8)";
        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { loaded = true; await Refresh(); timer.Start(); await LoadTelegram();
            if (Environment.GetCommandLineArgs().Contains("--snapshot")) await Dispatcher.InvokeAsync(SavePreview, DispatcherPriority.ContextIdle);
        };
        Closed += (_, _) => { timer.Stop(); http.Dispose(); };
    }

    private async Task Refresh()
    {
        if (refreshing || !loaded) return;
        refreshing = true;
        try
        {
            var data = await Task.Run(() =>
            {
                var settings = Settings.Load(root); var control = new ServiceController(root, settings.DataDirectory);
                return (Settings: settings, Control: control, State: control.Read(), Snapshot: DashboardReader.Read(settings, DateTimeOffset.UtcNow), Logs: ReadLogs(settings));
            });
            controller = data.Control; var state = data.State; snapshot = data.Snapshot;
            var running = state.ProcessId is not null;
            var stale = running && (state.Heartbeat is null || DateTimeOffset.UtcNow - state.Heartbeat > TimeSpan.FromSeconds(10));
            ServiceTitle.Text = busy ? "Выполняется команда…" : running ? stale ? "Процесс запущен · сигнал устарел" : "Сервис работает" : "Сервис остановлен";
            StateDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? stale ? "#E3A037" : "#18A77B" : "#94A3B8"));
            var uptime = state.Started is { } start ? FormatUptime(DateTimeOffset.UtcNow - start) : "—";
            ServiceDetails.Text = $"{(running ? $"PID {state.ProcessId} · в работе {uptime}" : "Обработка новых постов приостановлена")} · Задача Windows: {state.TaskState} · Автозапуск: {(state.AutoStart ? "включён" : "выключен")}";
            var sourceError = snapshot.Meta.GetValueOrDefault("sourceError");
            SourceHealth.Text = !string.IsNullOrWhiteSpace(sourceError) ? "Источник: " + sourceError : "Последнее успешное чтение: " + FormatTime(snapshot.Meta.GetValueOrDefault("sourcePollUtc")) + " · Сигнал процесса: " + (state.Heartbeat is { } h ? FormatTime(h.ToString("O")) : "нет данных");
            ChannelSummary.Text = $"{snapshot.Target}  ←  @{snapshot.Source}  ·  {(snapshot.AutoPublish ? "Автопубликация" : "Согласование в Telegram")}";
            PublishedCount.Text = snapshot.Posts.Count(p => p.State == "Published").ToString();
            QueueCount.Text = snapshot.Posts.Count(p => p.State is "Pending" or "Processing" or "Ready" or "Publishing" or "Notifying" or "Review").ToString();
            ReviewCount.Text = "на согласовании: " + snapshot.Posts.Count(p => p.State == "Review");
            IssueCount.Text = snapshot.Posts.Count(p => p.State is "Failed" or "Uncertain").ToString();
            AiUsage.Text = $"{snapshot.AttemptsToday} / {snapshot.Limit}";
            ConfigurationText.Text = $"Канал: {snapshot.Target}\nДонор: @{snapshot.Source}\nМодель ИИ: {snapshot.Model}\nРежим: {(snapshot.AutoPublish ? "автоматическая публикация" : "проверка черновиков владельцем")}\nИнтервал чтения: {snapshot.PollSeconds} секунд\nЛимит: {snapshot.Limit} попыток генерации в сутки UTC\nПрочитано до ID: {snapshot.Meta.GetValueOrDefault("cursor", "—")}\nПропущено материалов: {snapshot.Posts.Count(p => p.State == "Skipped")}\nДанные: {data.Settings.DataDirectory}";
            var nextKey = string.Join('|', snapshot.Posts.Select(p => $"{p.Id}:{p.State}:{p.Draft.Revision}:{p.Error}"));
            if (nextKey != rowsKey || PostsGrid.ItemsSource is null) { rowsKey = nextKey; ApplyFilter(); }
            if (LogText.Text != data.Logs)
            {
                var offset = LogText.VerticalOffset; var follow = offset >= LogText.ExtentHeight - LogText.ViewportHeight - 20;
                LogText.Text = data.Logs; if (follow) LogText.ScrollToEnd(); else LogText.ScrollToVerticalOffset(offset);
            }
            StartButton.IsEnabled = !busy && !running; StopButton.IsEnabled = !busy && (running || state.TaskState is "Работает" or "Ожидание запуска"); RestartButton.IsEnabled = !busy;
            Footer.Text = state.Error ?? $"Обновлено {TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Kyiv):HH:mm:ss} · автообновление каждые 3 с · закрытие окна не останавливает сервис";
        }
        catch (Exception e) { Footer.Text = "Не удалось обновить: " + e.Message; }
        finally { refreshing = false; }
    }

    private async Task Control(Func<Task> command, string success)
    {
        if (busy || controller is null) return;
        busy = true; StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = false;
        ServiceTitle.Text = "Выполняется команда…";
        try { await Task.Run(command); Footer.Text = success; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Управление сервисом", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { busy = false; await Refresh(); }
    }
    private async void StartClick(object sender, RoutedEventArgs e) => await Control(() => controller.Start(), "Сервис запущен.");
    private async void StopClick(object sender, RoutedEventArgs e) => await Control(() => controller.Stop(), "Сервис остановлен.");
    private async void RestartClick(object sender, RoutedEventArgs e) => await Control(async () => { await controller.Stop(); await controller.Start(); }, "Сервис перезапущен.");
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Refresh();

    private void ApplyFilter()
    {
        if (snapshot is null) return;
        var selected = (PostsGrid.SelectedItem as PostOverview)?.Id;
        var query = SearchBox.Text.Trim(); var category = StateFilter.SelectedIndex;
        var rows = snapshot.Posts.Where(p => (query.Length == 0 || p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Contains(query)) && category switch
        {
            1 => p.State is "Pending" or "Processing" or "Ready" or "Review" or "Publishing" or "Notifying", 2 => p.State == "Published", 3 => p.State is "Failed" or "Uncertain", 4 => p.State == "Skipped", _ => true
        }).ToArray();
        PostsGrid.ItemsSource = rows; PostsGrid.SelectedItem = rows.FirstOrDefault(p => p.Id == selected) ?? rows.FirstOrDefault();
    }
    private void FilterChanged(object sender, RoutedEventArgs e) { if (loaded) ApplyFilter(); }
    private void PostSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PostsGrid.SelectedItem is not PostOverview p) { DraftText.Text = "Выберите публикацию слева."; OriginalText.Text = NotesText.Text = ""; return; }
        DraftText.Text = string.IsNullOrWhiteSpace(p.Draft.Text) ? "Результат ещё не подготовлен." : p.Draft.Text;
        OriginalText.Text = p.Draft.Source.Text;
        NotesText.Text = $"Состояние: {p.Status}\nВерсия: {p.Draft.Revision}\n\n{p.Draft.Notes}\n\n{p.Error}";
    }

    private async Task LoadTelegram()
    {
        if (telegramBusy) return; telegramBusy = true; TelegramInfo.Text = "Запрашиваем данные канала…";
        try
        {
            var settings = Settings.Load(root); var bot = new TelegramBot(http, settings.BotToken);
            var info = await bot.Call("getChat", new { chat_id = settings.TargetChannel }, default);
            var members = await bot.Call("getChatMemberCount", new { chat_id = settings.TargetChannel }, default);
            TelegramInfo.Text = $"Канал: {info.GetProperty("title").GetString()}\nПодписчиков: {members.GetInt32()} (включая бота)\nПроверено: {TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Kyiv):dd.MM.yyyy HH:mm:ss}";
        }
        catch (Exception e) { TelegramInfo.Text = "Telegram сейчас недоступен: " + e.Message; }
        finally { telegramBusy = false; }
    }
    private async void TelegramRefresh(object sender, RoutedEventArgs e) => await LoadTelegram();
    private void OpenChannel(object sender, RoutedEventArgs e) => Open("https://t.me/" + (snapshot?.Target ?? "@ponyatny_dotnet_diary").TrimStart('@'));
    private void OpenBot(object sender, RoutedEventArgs e) => Open("https://t.me/ponyatny_dotnet_editor_bot");
    private void OpenProject(object sender, RoutedEventArgs e) => Open(root);
    private void OpenPrompt(object sender, RoutedEventArgs e) { try { var psi = new ProcessStartInfo("notepad.exe") { UseShellExecute = true }; psi.ArgumentList.Add(Settings.Load(root).PromptFile); Process.Start(psi); } catch (Exception ex) { Footer.Text = ex.Message; } }
    private void OpenLogs(object sender, RoutedEventArgs e) => Open(Path.Combine(Settings.Load(root).DataDirectory, "logs"));
    private void OpenSource(object sender, RoutedEventArgs e) { if (PostsGrid.SelectedItem is PostOverview p) Open(p.Draft.Source.Url); }
    private void OpenPublication(object sender, RoutedEventArgs e) { if (PostsGrid.SelectedItem is PostOverview { PublicationUrl: { } url }) Open(url); else Footer.Text = "У выбранного поста ещё нет ссылки на публикацию."; }
    private void Open(string destination) { try { Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true }); } catch (Exception e) { Footer.Text = e.Message; } }

    private static string ReadLogs(Settings settings)
    {
        var directory = Path.Combine(settings.DataDirectory, "logs"); if (!Directory.Exists(directory)) return "Журналов пока нет.";
        var result = new StringBuilder();
        foreach (var file in new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(4))
        {
            result.AppendLine($"── {file.Name} ──");
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 65536) stream.Seek(-65536, SeekOrigin.End);
            using var reader = new StreamReader(stream); if (stream.Position > 0) reader.ReadLine();
            var text = reader.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(settings.BotToken)) text = text.Replace(settings.BotToken, "[скрыто]");
            if (!string.IsNullOrWhiteSpace(settings.OpenAiKey)) text = text.Replace(settings.OpenAiKey, "[скрыто]");
            result.AppendLine(string.Join(Environment.NewLine, text.Split('\n').TakeLast(120))); result.AppendLine();
        }
        return result.ToString();
    }
    private static string FormatTime(string? value) => DateTimeOffset.TryParse(value, out var time) ? TimeZoneInfo.ConvertTime(time, Kyiv).ToString("dd.MM HH:mm:ss") : "нет данных";
    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays} д {uptime.Hours} ч" : $"{(int)uptime.TotalHours} ч {uptime.Minutes} мин";
    private void AddShortcut(Key key, RoutedEventHandler handler)
    {
        var command = new RoutedCommand(); CommandBindings.Add(new CommandBinding(command, (s, e) => handler(s, e)));
        InputBindings.Add(new KeyBinding(command, new KeyGesture(key)));
    }
    private void SavePreview()
    {
        var visual = (Visual)Content;
        var content = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(root, "data", "admin-preview.png")); encoder.Save(file);
    }
    private static string LocateRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "settings.example.json"))) return d.FullName;
        throw new DirectoryNotFoundException("Поместите админку внутрь папки DotNetChannel.");
    }
}
