using Flow.Application.Features.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Контекстная шапка чанка — по тесту на каждый из четырёх типов источников.</summary>
public class ChunkHeaderBuilderTests
{
    [Fact]
    public void ForTask_PutsCodeBeforeTitle() =>
        Assert.Equal("[PROJ-142] Падает экспорт отчёта в PDF", ChunkHeaderBuilder.ForTask("PROJ-142", "Падает экспорт отчёта в PDF"));

    [Fact]
    public void ForComment_NamesBoardAndTask() =>
        Assert.Equal("Проект «Доставка» · PROJ-142 · комментарий", ChunkHeaderBuilder.ForComment("Доставка", "PROJ-142"));

    [Fact]
    public void ForBoard_NamesProjectAndKey() =>
        Assert.Equal("Проект «Доставка» (PROJ)", ChunkHeaderBuilder.ForBoard("Доставка", "PROJ"));

    [Fact]
    public void ForUser_JoinsUsernameAndFullName() =>
        Assert.Equal("@ilya · Илья Моторин", ChunkHeaderBuilder.ForUser("ilya", "Илья", "Моторин"));

    [Fact]
    public void ForUser_KeepsOnlyUsername_WhenNameIsEmpty() =>
        Assert.Equal("@ilya", ChunkHeaderBuilder.ForUser("ilya", null, null));

    [Fact]
    public void Apply_PutsHeaderBeforeContent() =>
        Assert.Equal("[PROJ-1] Название\nТекст чанка", ChunkHeaderBuilder.Apply("[PROJ-1] Название", "Текст чанка"));
}
