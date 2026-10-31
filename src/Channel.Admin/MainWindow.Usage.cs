using System.Text;
using Channel.Infrastructure;

namespace Channel.Admin;

public partial class MainWindow
{
    private void RefreshUsage()
    {
        if (snapshot?.TokenUsage is not { } usage) return;
        TokensToday.Text = usage.TodayTokens == 0 && usage.TodayUnrecordedAttempts > 0 ? T("Не записано") : usage.TodayTokens.ToString("N0", UiText.Culture(uiLanguage)) + (usage.TodayUnrecordedAttempts > 0 ? "+" : "");
        TokensAllTime.Text = usage.AllTimeTokens == 0 && usage.AllTimeUnrecordedAttempts > 0 ? T("Не записано") : usage.AllTimeTokens.ToString("N0", UiText.Culture(uiLanguage)) + (usage.AllTimeUnrecordedAttempts > 0 ? "+" : "");
        RequestsRemaining.Text = Math.Max(0, snapshot.Limit - snapshot.AttemptsToday).ToString();
        TokensRemaining.Text = T("Лимит не задан");
        UsageNotice.Text = T("Здесь показан только расход, полученный от API. Старые запросы и ответы без usage не считаются нулевыми.") +
            (usage.TodayUnrecordedAttempts > 0 ? "\n" + T("Запросов без записанного расхода сегодня: {0}. Итоги могут быть неполными.", usage.TodayUnrecordedAttempts) : "");
        var recent = usage.Entries.FirstOrDefault(e => e.Report.RateLimitRemainingTokens is not null);
        RateWindow.Text = recent is null ? T("Остаток окна скорости API ещё не получен. Это не баланс аккаунта OpenAI.") :
            T("Окно скорости API: {0:N0} токенов по последнему ответу ({1}); сброс: {2}. Это не баланс аккаунта.", recent.Report.RateLimitRemainingTokens, FormatTime(recent.CreatedUtc.ToString("O")), recent.Report.RateLimitReset ?? "—");
        UsageGrid.ItemsSource = usage.Entries.Select(e => new { e.PostId, e.Revision, e.Model, Date = FormatTime(e.CreatedUtc.ToString("O")), Input = e.Report.InputTokens?.ToString("N0") ?? "—", Output = e.Report.OutputTokens?.ToString("N0") ?? "—", Total = e.Report.TotalTokens?.ToString("N0") ?? "—", Status = e.Report.Status });
        RefreshSelectedUsage();
    }

    private void RefreshSelectedUsage()
    {
        if (PostsGrid.SelectedItem is not PostOverview post || snapshot?.TokenUsage is not { } all) { ArticleUsage.Text = T("Выберите публикацию слева."); return; }
        var usage = post.TokenUsage;
        if (usage is null || usage.Attempts == 0) { ArticleUsage.Text = T("Расход токенов для этой статьи не записывался. Старые значения восстановить из сохранённого текста нельзя."); return; }
        if (usage.RecordedAttempts == 0) { ArticleUsage.Text = T("API не вернул расход; значение неизвестно.") + "\n" + T("Запросов без usage: {0}", usage.Attempts); return; }
        var sb = new StringBuilder();
        sb.AppendLine(T("Входные токены: {0:N0}\nВыходные токены: {1:N0}\nВсего записано: {2:N0}\nИз входных — кэш: {3:N0}\nИз выходных — рассуждения: {4:N0}\nПопыток с usage: {5} из {6}", usage.InputTokens, usage.OutputTokens, usage.TotalTokens, usage.CachedInputTokens, usage.ReasoningTokens, usage.RecordedAttempts, usage.Attempts));
        sb.AppendLine(); sb.AppendLine(T("Кэш и рассуждения уже включены во входные и выходные итоги. Сумма учитывает все записанные попытки этой статьи."));
        foreach (var e in all.Entries.Where(e => e.PostId == post.Id))
        {
            sb.AppendLine(); sb.AppendLine($"{FormatTime(e.CreatedUtc.ToString("O"))} · v{e.Revision} · {e.Model} · {e.Report.Status}");
            sb.AppendLine(e.Report.TotalTokens is { } total ? T("Вход: {0:N0} · Выход: {1:N0} · Всего: {2:N0}", e.Report.InputTokens, e.Report.OutputTokens, total) : T("API не вернул расход; значение неизвестно."));
        }
        ArticleUsage.Text = sb.ToString();
    }
    private void OpenAiUsage(object sender, System.Windows.RoutedEventArgs e) => Open("https://platform.openai.com/usage");
}
