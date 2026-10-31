using System.Text;
using System.Text.RegularExpressions;

namespace Channel.Infrastructure;

public record CodeEntity(string type, int offset, int length, string language);
public record RenderedText(string Text, CodeEntity[] Entities);
public static class TelegramText
{
    public static RenderedText Render(string source)
    {
        var text = new StringBuilder(); var entities = new List<CodeEntity>(); var previous = 0;
        foreach (Match match in Regex.Matches(source, @"```(?:([A-Za-z0-9_+#.-]+))?\r?\n([\s\S]*?)```"))
        {
            text.Append(source.AsSpan(previous, match.Index - previous));
            var code = match.Groups[2].Value.TrimEnd('\r', '\n');
            if (code.Length > 0) entities.Add(new("pre", text.Length, code.Length, match.Groups[1].Value));
            text.Append(code); previous = match.Index + match.Length;
        }
        text.Append(source.AsSpan(previous));
        return new(text.ToString(), entities.ToArray());
    }
}
