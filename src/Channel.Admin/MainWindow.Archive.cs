using System.IO;
using System.Windows;
using System.Windows.Controls;
using Channel.Infrastructure;

namespace Channel.Admin;

public partial class MainWindow
{
    private List<ArchiveRow> archiveRows = [];
    private CancellationTokenSource? archiveCancellation;

    private void InitializeArchive()
    {
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Kyiv).Date;
        ArchiveFrom.SelectedDate = today.AddDays(-6); ArchiveTo.SelectedDate = today;
        UpdateArchiveMode();
        Closed += (_, _) => archiveCancellation?.Cancel();
    }

    private void ArchiveModeChanged(object sender, SelectionChangedEventArgs e) { if (loaded) UpdateArchiveMode(); }
    private void UpdateArchiveMode()
    {
        var period = ArchiveMode.SelectedIndex == 0;
        ArchiveFrom.IsEnabled = ArchiveTo.IsEnabled = period; ArchiveLastCount.IsEnabled = !period;
    }
    private void ArchivePreset(object sender, RoutedEventArgs e)
    {
        var days = int.Parse((string)((Button)sender).Tag);
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Kyiv).Date;
        ArchiveMode.SelectedIndex = 0; ArchiveFrom.SelectedDate = today.AddDays(1 - days); ArchiveTo.SelectedDate = today;
    }
    private async void ArchiveScanClick(object sender, RoutedEventArgs e) => await ScanArchive();

    private async Task ScanArchive()
    {
        if (archiveCancellation is not null) return;
        archiveRows = []; ArchiveGrid.ItemsSource = null; UpdateArchiveSelection();
        try
        {
            ArchiveRequest request;
            if (ArchiveMode.SelectedIndex == 1)
            {
                if (!int.TryParse(ArchiveLastCount.Text, out var count)) throw new ArgumentException(T("Введите число последних текстовых постов от 1 до 1000."));
                request = new(LastPosts: count);
            }
            else
            {
                if (ArchiveFrom.SelectedDate is not { } from || ArchiveTo.SelectedDate is not { } to || from.Date > to.Date) throw new ArgumentException(T("Укажите начало и конец периода."));
                var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified), Kyiv);
                var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Unspecified), Kyiv);
                request = new(start, end);
            }
            request.Validate();
            archiveCancellation = new(); ArchiveScanButton.IsEnabled = false; ArchiveQueueButton.IsEnabled = false; ArchiveCancelButton.IsEnabled = true;
            ArchiveStatus.Text = T("Читаем публичный архив. ИИ и публикация не запускаются…");
            var settings = Settings.Load(root);
            var scanner = new ArchiveScanner(new TelegramSource(http, settings.SourceChannel));
            var progress = new Progress<ArchiveProgress>(p => ArchiveStatus.Text = T("Прочитано страниц: {0}. Постов в выборке: {1}. Сканирование без ИИ…", p.Pages, p.Found));
            var result = await scanner.Scan(request, progress, archiveCancellation.Token);
            LoadArchiveRows(result.Posts, settings);
            ArchiveStatus.Text = T("Найдено текстовых постов: {0}; новых: {1}; уже учтено: {2}. Страниц: {3}.", result.Posts.Count, archiveRows.Count(r => r.CanQueue), archiveRows.Count(r => !r.CanQueue), result.Pages) + (result.Warning is { } warning ? "\n" + ErrorText(warning) : "");
        }
        catch (OperationCanceledException) { ArchiveStatus.Text = T("Сканирование отменено. Очередь не изменялась."); }
        catch (Exception e) { ArchiveStatus.Text = T("Сканирование не завершено: ") + ErrorText(e.Message); }
        finally
        {
            archiveCancellation?.Dispose(); archiveCancellation = null;
            ArchiveScanButton.IsEnabled = true; ArchiveCancelButton.IsEnabled = false; UpdateArchiveSelection();
        }
    }

    private void LoadArchiveRows(IEnumerable<Channel.Core.SourcePost> posts, Settings settings)
    {
        var known = DashboardReader.Read(settings, DateTimeOffset.UtcNow).Posts.ToDictionary(p => p.Id, p => p.Status);
        archiveRows = posts.OrderByDescending(p => p.Id).Select(p => new ArchiveRow(p, known.GetValueOrDefault(p.Id), Kyiv, uiLanguage)).ToList();
        foreach (var row in archiveRows) row.PropertyChanged += (_, _) => UpdateArchiveSelection();
        ArchiveGrid.ItemsSource = archiveRows; ArchiveGrid.SelectedItem = archiveRows.FirstOrDefault(); UpdateArchiveSelection();
    }

    private void UpdateArchiveSelection()
    {
        var count = archiveRows.Count(r => r.IsSelected && r.CanQueue);
        ArchiveQueueButton.Content = T("Добавить в очередь ({0})", count);
        ArchiveQueueButton.IsEnabled = archiveCancellation is null && count > 0;
    }
    private void ArchiveCancelClick(object sender, RoutedEventArgs e) => archiveCancellation?.Cancel();
    private void ArchiveSelectNew(object sender, RoutedEventArgs e) { foreach (var row in archiveRows) row.IsSelected = row.CanQueue; }
    private void ArchiveClearSelection(object sender, RoutedEventArgs e) { foreach (var row in archiveRows) row.IsSelected = false; }
    private void ArchivePostSelected(object sender, SelectionChangedEventArgs e) => ArchivePreview.Text = ArchiveGrid.SelectedItem is ArchiveRow row ? row.Source.Text : T("Выберите пост для просмотра.");

    private async void ArchiveQueueClick(object sender, RoutedEventArgs e)
    {
        var selected = archiveRows.Where(r => r.IsSelected && r.CanQueue).Select(r => r.Source).ToArray();
        if (selected.Length == 0 || archiveCancellation is not null) return;
        ArchiveQueueButton.IsEnabled = false;
        try
        {
            var settings = Settings.Load(root);
            var result = await Task.Run(() => ArchiveScanner.Enqueue(settings, selected));
            LoadArchiveRows(archiveRows.Select(r => r.Source).ToArray(), settings);
            await Refresh();
            var state = await Task.Run(() => new ServiceController(root, settings.DataDirectory).Read());
            ArchiveStatus.Text = T("Добавлено в очередь: {0}. Уже были учтены: {1}. Повторная генерация для них не запускается.\n", result.Added, result.AlreadyKnown) +
                (state.ProcessId is null ? T("Сервис остановлен — нажмите «Запустить» для обработки. ") : T("Сервис обработает очередь по текущему режиму публикации. ")) +
                (snapshot is { } s && s.AttemptsToday >= s.Limit ? T("Дневной лимит ИИ достигнут: обработка продолжится после 00:00 UTC.") : T("Действует общий дневной лимит ИИ."));
        }
        catch (Exception error) { ArchiveStatus.Text = T("Не удалось добавить в очередь: ") + ErrorText(error.Message); }
        finally { UpdateArchiveSelection(); }
    }
}
