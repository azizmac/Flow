using System.Text;

namespace Flow.Application.Features.Search;

/// <summary>
/// Режет текст источника на чанки под окно эмбеддера. Границы выбираются по убыванию «смысловой цены»:
/// сначала абзацы, потом предложения, и только если и предложение не влезает — по символам.
/// Токены считаются приближённо (<see cref="CharsPerToken"/> символа на токен — эмпирика для русского):
/// точный токенизатор Qwen в этой ветке не нужен, оценка сверху безопаснее, чем зависимость от модели.
/// Чистая функция без состояния — покрыта юнит-тестами.
/// </summary>
public static class TextChunker
{
    /// <summary>Символов на токен. Русский текст в BPE даёт примерно столько; для латиницы оценка консервативна.</summary>
    public const int CharsPerToken = 3;

    private static readonly char[] SentenceEnders = ['.', '!', '?', '…', ';'];

    /// <summary>
    /// Пустой текст — пустой список (пустые чанки в индекс не попадают). Текст короче лимита — один чанк.
    /// </summary>
    public static IReadOnlyList<string> Split(string? text, int chunkTokens, int chunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        ArgumentOutOfRangeException.ThrowIfLessThan(chunkTokens, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);

        var maxChars = chunkTokens * CharsPerToken;
        // Перекрытие ≥ размера чанка съело бы весь прогресс: следующий чанк начинался бы там же, где предыдущий.
        var overlapChars = Math.Min(chunkOverlap, Math.Max(chunkTokens - 1, 0)) * CharsPerToken;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxChars)
            return [normalized];

        var chunks = Pack(Atomize(normalized, maxChars), maxChars);
        return overlapChars == 0 ? chunks : ApplyOverlap(chunks, overlapChars);
    }

    /// <summary>Абзацы; слишком длинный абзац — предложения; слишком длинное предложение — срез по символам.</summary>
    private static List<string> Atomize(string text, int maxChars)
    {
        var atoms = new List<string>();

        foreach (var paragraph in SplitParagraphs(text))
        {
            if (paragraph.Length <= maxChars)
            {
                atoms.Add(paragraph);
                continue;
            }

            foreach (var sentence in SplitSentences(paragraph))
            {
                if (sentence.Length <= maxChars)
                    atoms.Add(sentence);
                else
                    atoms.AddRange(SplitHard(sentence, maxChars));
            }
        }

        return atoms;
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    /// <summary>Конец предложения — знак из <see cref="SentenceEnders"/>, за которым пробел или конец текста.</summary>
    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        var start = 0;

        for (var i = 0; i < paragraph.Length; i++)
        {
            if (Array.IndexOf(SentenceEnders, paragraph[i]) < 0)
                continue;

            // Точка внутри "1.5" или "т.е." концом предложения не считается: нужен пробел следом.
            var isBoundary = i + 1 == paragraph.Length || char.IsWhiteSpace(paragraph[i + 1]);
            if (!isBoundary)
                continue;

            var sentence = paragraph[start..(i + 1)].Trim();
            if (sentence.Length > 0)
                yield return sentence;

            start = i + 1;
        }

        var tail = paragraph[start..].Trim();
        if (tail.Length > 0)
            yield return tail;
    }

    /// <summary>Последнее средство: режем по пробелу перед лимитом, а если пробела нет — ровно по лимиту.</summary>
    private static IEnumerable<string> SplitHard(string text, int maxChars)
    {
        var position = 0;

        while (position < text.Length)
        {
            var length = Math.Min(maxChars, text.Length - position);

            if (position + length < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', position + length - 1, length);
                if (lastSpace > position)
                    length = lastSpace - position;
            }

            var piece = text.Substring(position, length).Trim();
            if (piece.Length > 0)
                yield return piece;

            position += length;

            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }
    }

    private static List<string> Pack(List<string> atoms, int maxChars)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var atom in atoms)
        {
            var separator = current.Length == 0 ? string.Empty : "\n\n";

            if (current.Length + separator.Length + atom.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
                separator = string.Empty;
            }

            current.Append(separator).Append(atom);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    /// <summary>
    /// Дописывает в начало каждого чанка хвост предыдущего: запрос, попавший на стык, всё равно найдёт
    /// хотя бы один чанк с полным контекстом. Обрезается по пробелу, чтобы не рвать слово.
    /// </summary>
    private static List<string> ApplyOverlap(List<string> chunks, int overlapChars)
    {
        var result = new List<string>(chunks.Count) { chunks[0] };

        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1];
            var tail = previous.Length <= overlapChars ? previous : previous[^overlapChars..];

            var firstSpace = tail.IndexOf(' ');
            if (firstSpace >= 0 && firstSpace + 1 < tail.Length)
                tail = tail[(firstSpace + 1)..];

            result.Add($"{tail.Trim()}\n\n{chunks[i]}");
        }

        return result;
    }
}
