using Flow.Application.Features.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Чанкер и шапки чанков (ТЗ поиска, этап 3.2). Чистые функции — без DI и БД.</summary>
public class TextChunkerTests
{
    // 512 токенов × 3 символа = 1536 символов в чанке.
    private const int ChunkTokens = 512;
    private const int ChunkOverlap = 64;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Split_Should_Return_Nothing_For_Empty_Text(string? text) =>
        Assert.Empty(TextChunker.Split(text, ChunkTokens, ChunkOverlap));

    [Fact]
    public void Split_Should_Return_Single_Chunk_When_Text_Fits()
    {
        var chunks = TextChunker.Split("  Падает экспорт отчёта в PDF  ", ChunkTokens, ChunkOverlap);

        Assert.Equal(["Падает экспорт отчёта в PDF"], chunks);
    }

    [Fact]
    public void Split_Should_Cut_On_Paragraph_Boundaries()
    {
        // Два абзаца, каждый чуть больше половины лимита: в один чанк они не влезают, и резать их
        // надо ровно по границе абзаца, а не посередине предложения.
        var first = new string('а', 1000);
        var second = new string('б', 1000);

        var chunks = TextChunker.Split($"{first}\n\n{second}", ChunkTokens, chunkOverlap: 0);

        Assert.Equal([first, second], chunks);
    }

    [Fact]
    public void Split_Should_Fall_Back_To_Sentences_When_Paragraph_Too_Long()
    {
        var sentence = new string('а', 900) + ".";
        var paragraph = $"{sentence} {sentence}";

        var chunks = TextChunker.Split(paragraph, ChunkTokens, chunkOverlap: 0);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(sentence, chunk));
    }

    [Fact]
    public void Split_Should_Hard_Cut_Word_Longer_Than_Limit()
    {
        var word = new string('я', 4000);

        var chunks = TextChunker.Split(word, ChunkTokens, chunkOverlap: 0);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= ChunkTokens * TextChunker.CharsPerToken));
        Assert.Equal(word.Length, chunks.Sum(chunk => chunk.Length));
    }

    [Fact]
    public void Split_Should_Overlap_Chunks()
    {
        var first = string.Join(' ', Enumerable.Repeat("альфа", 200));
        var second = string.Join(' ', Enumerable.Repeat("бета", 200));

        var chunks = TextChunker.Split($"{first}\n\n{second}", ChunkTokens, ChunkOverlap);

        Assert.Equal(2, chunks.Count);
        // Второй чанк начинается хвостом первого: запрос, попавший на стык, найдёт полный контекст.
        Assert.StartsWith("альфа", chunks[1]);
        Assert.Contains("бета", chunks[1]);
    }

    [Fact]
    public void Split_Should_Not_Loop_When_Overlap_Exceeds_Chunk()
    {
        var text = string.Join("\n\n", Enumerable.Repeat(new string('а', 1000), 4));

        var chunks = TextChunker.Split(text, chunkTokens: 400, chunkOverlap: 4000);

        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count <= 8);
    }
}

public class ChunkHeaderBuilderTests
{
    [Fact]
    public void Task_Header_Should_Combine_Code_And_Title() =>
        Assert.Equal("[PROJ-142] Падает экспорт отчёта в PDF",
            ChunkHeaderBuilder.ForTask("PROJ-142", "  Падает экспорт отчёта в PDF "));

    [Fact]
    public void Comment_Header_Should_Name_Project_And_Task() =>
        Assert.Equal("Проект «Флоу» · PROJ-142 · комментарий",
            ChunkHeaderBuilder.ForComment("Флоу", "PROJ-142"));

    [Fact]
    public void Board_Header_Should_Carry_Key() =>
        Assert.Equal("Проект «Флоу» (PROJ)", ChunkHeaderBuilder.ForBoard("Флоу", "PROJ"));

    [Fact]
    public void User_Header_Should_Combine_Username_And_Name() =>
        Assert.Equal("@ilya · Илья Моторин", ChunkHeaderBuilder.ForUser("ilya", "Илья Моторин"));

    [Fact]
    public void User_Header_Should_Skip_Empty_Name() =>
        Assert.Equal("@ilya", ChunkHeaderBuilder.ForUser("ilya", "   "));
}
