using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Flow.Client.Markdown;

/// <summary>
/// «@username» → ссылка на профиль (/u/{username} → редирект на /users/{id}) с классом mention. Те же правила, что у MentionParser
/// на сервере: username по регулярке User, перед @ не буква/цифра/«/» (e-mail и пути — не упоминания).
/// Код (`…`, ```…```) Markdig разбирает раньше, сюда его содержимое не попадает.
/// </summary>
public sealed class MentionInlineParser : InlineParser
{
    private const int MaxLength = 32;

    public MentionInlineParser()
    {
        OpeningCharacters = ['@'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var previous = slice.PeekCharExtra(-1);
        if (char.IsLetterOrDigit(previous) || previous is '/' or '_' or '.')
            return false;

        var text = slice.Text;
        var start = slice.Start + 1;
        var i = start;
        while (i <= slice.End && IsUsernameChar(text[i]) && i - start < MaxLength)
            i++;

        // Хвостовые разделители не входят в имя: «@ilya.» — это @ilya и точка.
        while (i > start && text[i - 1] is '.' or '-' or '_')
            i--;

        var length = i - start;
        if (length < 2 || (i <= slice.End && (char.IsLetterOrDigit(text[i]) || text[i] == '-')))
            return false;

        var username = text.Substring(start, length).ToLowerInvariant();
        var link = new LinkInline($"u/{username}", string.Empty)
        {
            IsClosed = true,
            Span = new Markdig.Syntax.SourceSpan(processor.GetSourcePosition(slice.Start, out var line, out var column), processor.GetSourcePosition(i - 1)),
            Line = line,
            Column = column
        };
        link.AppendChild(new LiteralInline("@" + username));
        link.GetAttributes().AddClass("mention");

        processor.Inline = link;
        slice.Start = i;
        return true;
    }

    private static bool IsUsernameChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-';
}
