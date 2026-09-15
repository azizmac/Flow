using System.Text;

namespace Flow.Application.Features.Search;

/// <summary>
/// Режет длинный текст на чанки под контекст эмбеддера: сначала по границам абзацев, внутри длинного
/// абзаца — по предложениям, и только совсем длинное предложение рубится по символам. Токены считаются
/// приближённо (символы ÷ 3 — оценка для русского): точный BPE-токенизатор здесь не нужен, лимит
/// модели (32k) на порядок больше размера чанка, и ошибка оценки в него укладывается.
/// </summary>
public static class TextChunker
{
    /// <summary>Сколько символов приходится на токен в русском тексте (грубая оценка).</summary>
    public const int CharsPerToken = 3;

    private static readonly char[] SentenceEnders = ['.', '!', '?', '…', ';'];

    /// <summary>
    /// Пустой или пробельный текст даёт пустой список — чанков без содержимого не бывает.
    /// Текст короче лимита остаётся одним чанком (самый частый случай: название задачи, комментарий).
    /// </summary>
    public static IReadOnlyList<string> Split(string? text, int chunkTokens, int chunkOverlap)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return [];

        var maxChars = Math.Max(1, chunkTokens) * CharsPerToken;
        if (normalized.Length <= maxChars)
            return [normalized];

        var overlapChars = Math.Clamp(chunkOverlap, 0, Math.Max(0, chunkTokens - 1)) * CharsPerToken;

        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentHasPiece = false;

        foreach (var (separator, piece) in SplitToPieces(normalized, maxChars))
        {
            var wouldBe = current.Length + (current.Length > 0 ? separator.Length : 0) + piece.Length;
            if (currentHasPiece && wouldBe > maxChars)
            {
                var completed = current.ToString();
                chunks.Add(completed);

                // Перекрытие: следующий чанк начинается хвостом предыдущего, чтобы фраза на стыке
                // не потерялась. Хвост в лимит не укладывают — иначе на длинном куске чанк схлопнется в него.
                current.Clear();
                current.Append(Tail(completed, overlapChars));
                currentHasPiece = false;
            }

            if (current.Length > 0)
                current.Append(separator);

            current.Append(piece);
            currentHasPiece = true;
        }

        if (currentHasPiece)
            chunks.Add(current.ToString());

        return chunks;
    }

    private static string Normalize(string? text) =>
        text is null ? string.Empty : text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>Хвост предыдущего чанка, обрезанный до границы слова (слово в перекрытии важнее пары символов).</summary>
    private static string Tail(string chunk, int overlapChars)
    {
        if (overlapChars <= 0 || chunk.Length == 0)
            return string.Empty;

        var tail = chunk.Length <= overlapChars ? chunk : chunk[^overlapChars..];

        var wordStart = tail.IndexOfAny([' ', '\n']);
        if (wordStart >= 0 && tail.Length > overlapChars / 2)
            tail = tail[(wordStart + 1)..];

        return tail.TrimStart();
    }

    /// <summary>
    /// Текст → последовательность кусков со «своим» разделителем: абзацы склеиваются пустой строкой,
    /// предложения внутри абзаца — пробелом. Куски заведомо не длиннее лимита.
    /// </summary>
    private static IEnumerable<(string Separator, string Piece)> SplitToPieces(string text, int maxChars)
    {
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.Length <= maxChars)
            {
                yield return ("\n\n", trimmed);
                continue;
            }

            var first = true;
            foreach (var sentence in SplitToSentences(trimmed, maxChars))
            {
                yield return (first ? "\n\n" : " ", sentence);
                first = false;
            }
        }
    }

    private static IEnumerable<string> SplitToSentences(string paragraph, int maxChars)
    {
        var start = 0;
        while (start < paragraph.Length)
        {
            var end = FindSentenceEnd(paragraph, start, maxChars);
            var sentence = paragraph[start..end].Trim();
            if (sentence.Length > 0)
                yield return sentence;

            start = end;
        }
    }

    /// <summary>
    /// Конец очередного предложения, но не дальше лимита. Если предложение в лимит не влезает
    /// (сплошная простыня без точек, длинное слово, base64), режем по последнему пробелу, а когда
    /// и его нет — ровно по лимиту: чанк длиннее контекста модели хуже, чем разрезанное слово.
    /// </summary>
    private static int FindSentenceEnd(string text, int start, int maxChars)
    {
        var limit = Math.Min(text.Length, start + maxChars);
        if (limit == text.Length && limit - start <= maxChars)
            return limit;

        var lastEnder = -1;
        for (var i = start; i < limit; i++)
        {
            if (Array.IndexOf(SentenceEnders, text[i]) >= 0 && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
                lastEnder = i + 1;
        }

        if (lastEnder > start)
            return lastEnder;

        var lastSpace = text.LastIndexOfAny([' ', '\n'], limit - 1, limit - start);
        return lastSpace > start ? lastSpace + 1 : limit;
    }
}
