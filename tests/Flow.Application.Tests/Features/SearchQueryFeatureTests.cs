using Flow.Application.Abstractions;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Shared.Contracts.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Решения хендлера поиска: какие половины гибрида выполнять, что делать при недоступной модели
/// и какие лимиты доезжают до репозитория. Сам SQL проверяют интеграционные тесты.
/// </summary>
public class SearchQueryFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static SearchQuery Query(string text = "падает экспорт", SearchMode mode = SearchMode.Hybrid, int limit = 20, int offset = 0) =>
        new(Owner, text, null, null, IncludeArchived: false, mode, limit, offset);

    [Fact]
    public async Task Hybrid_Runs_Both_Halves()
    {
        var (mediator, _, index, embeddings) = TestMediatorFactory.CreateWithSearch();

        var response = await mediator.Send(Query(), CancellationToken.None);

        Assert.NotNull(index.LastCriteria!.QueryEmbedding);
        Assert.True(index.LastCriteria.UseText);
        Assert.Equal(1, embeddings.Calls);
        Assert.False(response!.Degraded);
        Assert.Equal(SearchMode.Hybrid, response.Mode);
    }

    [Fact]
    public async Task Text_Mode_Does_Not_Touch_The_Model()
    {
        var (mediator, _, index, embeddings) = TestMediatorFactory.CreateWithSearch();

        var response = await mediator.Send(Query(mode: SearchMode.Text), CancellationToken.None);

        Assert.Null(index.LastCriteria!.QueryEmbedding);
        Assert.True(index.LastCriteria.UseText);
        Assert.Equal(0, embeddings.Calls);
        Assert.False(response!.Degraded);
    }

    [Fact]
    public async Task Semantic_Mode_Skips_The_Text_Half()
    {
        var (mediator, _, index, _) = TestMediatorFactory.CreateWithSearch();

        await mediator.Send(Query(mode: SearchMode.Semantic), CancellationToken.None);

        Assert.NotNull(index.LastCriteria!.QueryEmbedding);
        Assert.False(index.LastCriteria.UseText);
    }

    [Fact]
    public async Task Unavailable_Model_Degrades_To_Text()
    {
        var (mediator, _, index, embeddings) = TestMediatorFactory.CreateWithSearch();
        embeddings.Fails = true;

        var response = await mediator.Send(Query(), CancellationToken.None);

        // Погашенная модель — не 500: запрос доезжает на полнотексте, клиент видит degraded.
        Assert.Null(index.LastCriteria!.QueryEmbedding);
        Assert.True(index.LastCriteria.UseText);
        Assert.True(response!.Degraded);
        Assert.Equal(SearchMode.Text, response.Mode);
    }

    [Fact]
    public async Task Unavailable_Model_Turns_Semantic_Into_Text()
    {
        var (mediator, _, index, embeddings) = TestMediatorFactory.CreateWithSearch();
        embeddings.Fails = true;

        var response = await mediator.Send(Query(mode: SearchMode.Semantic), CancellationToken.None);

        // Иначе Semantic остался бы вовсе без половин и молча вернул пустую выдачу.
        Assert.True(index.LastCriteria!.UseText);
        Assert.Equal(SearchMode.Text, response!.Mode);
    }

    [Fact]
    public async Task Limit_Is_Clamped_And_Offset_Is_Not_Negative()
    {
        var (mediator, options, index, _) = TestMediatorFactory.CreateWithSearch();

        await mediator.Send(Query(limit: 500, offset: -10), CancellationToken.None);

        Assert.Equal(options.Query.MaxLimit, index.LastCriteria!.Limit);
        Assert.Equal(0, index.LastCriteria.Offset);
    }

    [Fact]
    public async Task Empty_Query_Is_Rejected()
    {
        var (mediator, _, _, _) = TestMediatorFactory.CreateWithSearch();

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(Query("   "), CancellationToken.None));
    }

    [Fact]
    public async Task Disabled_Search_Returns_Null()
    {
        var (mediator, options, index, _) = TestMediatorFactory.CreateWithSearch();
        options.Enabled = false;

        var response = await mediator.Send(Query(), CancellationToken.None);

        // Контроллер превратит это в 404: выключенного поиска для клиента не существует.
        Assert.Null(response);
        Assert.Null(index.LastCriteria);
    }

    [Fact]
    public async Task All_Types_Are_Searched_By_Default()
    {
        var (mediator, _, index, _) = TestMediatorFactory.CreateWithSearch();

        await mediator.Send(Query(), CancellationToken.None);

        // Вложения входят в поиск по умолчанию наравне с остальным: файл, который виден только
        // при явном фильтре «Файлы», для человека всё равно что не найден.
        Assert.Equal(
            [SearchSourceType.Task, SearchSourceType.Comment, SearchSourceType.Board, SearchSourceType.User, SearchSourceType.Attachment],
            index.LastCriteria!.Types.ToArray());
    }

    [Fact]
    public async Task Query_Is_Normalized_Before_Search()
    {
        var (mediator, _, index, _) = TestMediatorFactory.CreateWithSearch();

        await mediator.Send(Query("  падает   экспорт \n отчёта "), CancellationToken.None);

        // По нормализованной строке кэшируется вектор запроса — лишние пробелы не должны её множить.
        Assert.Equal("падает экспорт отчёта", index.LastCriteria!.Query);
    }

    [Fact]
    public async Task Hits_Are_Mapped_To_Response_Items()
    {
        var (mediator, _, index, _) = TestMediatorFactory.CreateWithSearch();
        var taskId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var updatedAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        index.Page = new SearchPage(
            [new SearchHit(SearchSourceType.Task, taskId, boardId, "Падает экспорт", "<mark>экспорт</mark> падает", 0.032, "FLW-1", updatedAt, null)],
            Total: 1);

        var response = await mediator.Send(Query(), CancellationToken.None);

        var item = Assert.Single(response!.Items);
        Assert.Equal(SearchSourceType.Task, item.SourceType);
        Assert.Equal(taskId, item.SourceId);
        Assert.Equal(boardId, item.BoardId);
        Assert.Equal("Падает экспорт", item.Title);
        Assert.Contains("<mark>", item.Snippet);
        Assert.Equal("FLW-1", item.TaskCode);
        Assert.Equal(updatedAt, item.UpdatedAt);
        Assert.Equal(1, response.Total);
    }
}
