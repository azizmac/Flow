using Flow.Application.Features.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Нарезка описаний на чанки: границы, перекрытие, вырожденные случаи.</summary>
public class TextChunkerTests
{
    /// <summary>Лимит в токенах; в символах он втрое больше (TextChunker.CharsPerToken).</summary>
    private const int Tokens = 20;

    private const int Overlap = 5;

    private const int MaxChars = Tokens * TextChunker.CharsPerToken;

    [Fact]
    public void Split_ReturnsNothing_ForEmptyText()
    {
        Assert.Empty(TextChunker.Split(null, Tokens, Overlap));
        Assert.Empty(TextChunker.Split(string.Empty, Tokens, Overlap));
        Assert.Empty(TextChunker.Split("   \n\n  ", Tokens, Overlap));
    }

    [Fact]
    public void Split_KeepsShortTextAsSingleChunk()
    {
        var chunks = TextChunker.Split("  Падает экспорт отчёта  ", Tokens, Overlap);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Падает экспорт отчёта", chunk);
    }

    [Fact]
    public void Split_CutsOnParagraphBoundaries()
    {
        var first = new string('а', MaxChars - 10);
        var second = new string('б', MaxChars - 10);

        var chunks = TextChunker.Split($"{first}\n\n{second}", Tokens, chunkOverlap: 0);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(first, chunks[0]);
        Assert.Equal(second, chunks[1]);
    }

    [Fact]
    public void Split_RepeatsTailOfPreviousChunk()
    {
        var first = new string('а', MaxChars - 10);
        var second = new string('б', MaxChars - 10);

        var chunks = TextChunker.Split($"{first}\n\n{second}", Tokens, Overlap);

        Assert.Equal(2, chunks.Count);
        // Второй чанк начинается хвостом первого: фраза на стыке абзацев не теряется.
        Assert.StartsWith(new string('а', Overlap * TextChunker.CharsPerToken / 2), chunks[1]);
        Assert.Contains(new string('б', 10), chunks[1]);
    }

    [Fact]
    public void Split_CutsLongParagraphOnSentences()
    {
        var sentence = new string('а', MaxChars / 2 - 2) + ".";
        var text = string.Join(' ', Enumerable.Repeat(sentence, 4));

        var chunks = TextChunker.Split(text, Tokens, chunkOverlap: 0);

        Assert.True(chunks.Count >= 2);
        // Ни один чанк не обрывается посреди предложения: все заканчиваются точкой.
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk));
    }

    [Fact]
    public void Split_CutsWordLongerThanLimit()
    {
        var word = new string('ы', MaxChars * 3);

        var chunks = TextChunker.Split(word, Tokens, chunkOverlap: 0);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= MaxChars, $"чанк длиной {chunk.Length} > {MaxChars}"));
        Assert.Equal(word, string.Concat(chunks));
    }

    [Fact]
    public void Split_NormalizesLineEndings()
    {
        var chunks = TextChunker.Split("Первый\r\n\r\nВторой", Tokens, Overlap);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Первый\n\nВторой", chunk);
    }
}
