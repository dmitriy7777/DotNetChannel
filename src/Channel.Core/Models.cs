namespace Channel.Core;

public record SourcePost(long Id, string Url, string Text, string[] Links);
public record Rewrite(bool Relevant, string Text, string ReviewNotes);
public record Draft(SourcePost Source, string Text = "", string Notes = "", int Revision = 1);
public record StoredDraft(long Id, string State, Draft Draft, string? Error);

public static class PostRules
{
    public static string Publication(Draft draft) => $"{draft.Text.Trim()}\n\nИсточник: {draft.Source.Url}";
    public static void Validate(Rewrite result, SourcePost source)
    {
        if (!result.Relevant) return;
        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidDataException("ИИ вернул пустой текст.");
        var length = Publication(new Draft(source, result.Text)).Length;
        if (length > 3900) throw new InvalidDataException($"Текст слишком длинный ({length}); публикация остановлена.");
    }
}
