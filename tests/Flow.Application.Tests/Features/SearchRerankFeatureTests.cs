using Flow.Application.Abstractions;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Tests.Fakes;
using Flow.Shared.Contracts.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Вторая ступень выдачи (docs/TZ_search_vector.md, «Реранкер»): когда она вызывается, сколько
/// кандидатов забирает и что происходит, когда модель молчит. Само качество меряет golden-set —
/// фейковая ступень здесь просто переворачивает порядок.
/// </summary>
public class SearchRerankFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static SearchQuery Query(bool rerank, int limit = 5, int offset = 0) =>
        new(Owner, "падает экспорт", null, null, IncludeArchived: false, SearchMode.Hybrid, limit, offset, rerank);

    private static SearchTestContext Ready(int hits = 10, bool enabled = true)
    {
        var context = TestMediatorFactory.CreateSearchContext();
        context.Options.Rerank.Enabled = enabled;
        context.Index.Page = new SearchPage(Hits(hits), hits);
        return context;
    }

    private static IReadOnlyList<SearchHit> Hits(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new SearchHit(
                SearchSourceType.Task,
                Guid.NewGuid(),
                null,
                $"Задача {index}",
                $"фрагмент {index}",
                Score: 1.0 / (index + 1),
                TaskCode: $"PROJ-{index}",
                UpdatedAt: DateTime.UtcNow,
                ParentId: null,
                Content: $"текст задачи {index}"))
            .ToArray();

    [Fact]
    public async Task Without_The_Flag_The_Model_Is_Not_Touched()
    {
        var context = Ready();

        var response = await context.Mediator.Send(Query(rerank: false), CancellationToken.None);

        Assert.Equal(0, context.Reranker.Calls);
        Assert.False(response!.Reranked);
        // Без второй ступени из индекса берётся ровно страница, а не окно кандидатов.
        Assert.Equal(5, context.Index.LastCriteria!.Limit);
    }

    [Fact]
    public async Task Rerank_Takes_The_Whole_Window_And_Reorders_It()
    {
        var context = Ready(hits: 10);
        var beforeFirst = context.Index.Page.Items[0].TaskCode;

        var response = await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        // Кандидатов берём окном TopN, а не страницей: переупорядочивать пять из пяти бессмысленно.
        Assert.Equal(context.Options.Rerank.TopN, context.Index.LastCriteria!.Limit);
        Assert.Equal(1, context.Reranker.Calls);
        Assert.True(response!.Reranked);
        Assert.Equal(5, response.Items.Count);
        // Фейк переворачивает порядок — первым становится последний кандидат, а не прежний первый.
        Assert.NotEqual(beforeFirst, response.Items[0].TaskCode);
        Assert.Equal("PROJ-9", response.Items[0].TaskCode);
    }

    [Fact]
    public async Task Turned_Off_In_Settings_The_Flag_Does_Nothing()
    {
        var context = Ready(enabled: false);

        var response = await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        Assert.Equal(0, context.Reranker.Calls);
        Assert.False(response!.Reranked);
        Assert.Equal(5, context.Index.LastCriteria!.Limit);
    }

    [Fact]
    public async Task Unconfigured_Model_Is_Not_Called()
    {
        var context = Ready();
        context.Reranker.IsConfigured = false;

        var response = await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        Assert.Equal(0, context.Reranker.Calls);
        Assert.False(response!.Reranked);
    }

    [Fact]
    public async Task Silent_Model_Leaves_The_Hybrid_Order()
    {
        var context = Ready();
        context.Reranker.Fails = true;
        var expected = context.Index.Page.Items.Take(5).Select(hit => hit.TaskCode).ToArray();

        var response = await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        // Недоступная вторая ступень — не ошибка запроса: выдача уже есть, она просто не уточнена.
        Assert.False(response!.Reranked);
        Assert.Equal(expected, response.Items.Select(item => item.TaskCode).ToArray());
    }

    [Fact]
    public async Task Empty_Answer_Leaves_The_Hybrid_Order()
    {
        var context = Ready();
        context.Reranker.ReturnsNothing = true;

        var response = await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        Assert.False(response!.Reranked);
        Assert.Equal("PROJ-0", response.Items[0].TaskCode);
    }

    [Fact]
    public async Task Pages_Beyond_The_First_Are_Not_Reranked()
    {
        var context = Ready();

        var response = await context.Mediator.Send(Query(rerank: true, offset: 20), CancellationToken.None);

        // Окно считается по первой странице; дальше выдача остаётся гибридной, и клиент видит это по Reranked.
        Assert.Equal(0, context.Reranker.Calls);
        Assert.False(response!.Reranked);
        Assert.Equal(20, context.Index.LastCriteria!.Offset);
    }

    [Fact]
    public async Task Model_Sees_Title_With_Content_And_Nothing_Longer_Than_The_Limit()
    {
        var context = Ready(hits: 3);
        context.Options.Rerank.MaxDocumentChars = 200;
        // Два кандидата, а не один: переупорядочивать нечего, когда результат единственный.
        context.Index.Page = new SearchPage(
            [
                new SearchHit(SearchSourceType.Task, Guid.NewGuid(), null, "Падает экспорт", "фрагмент", 1, "PROJ-1", DateTime.UtcNow, null, new string('я', 500)),
                new SearchHit(SearchSourceType.Task, Guid.NewGuid(), null, "Второй", "фрагмент", 0.5, "PROJ-2", DateTime.UtcNow, null, "коротко")
            ],
            2);

        await context.Mediator.Send(Query(rerank: true), CancellationToken.None);

        var document = context.Reranker.LastDocuments[0];
        Assert.StartsWith("Падает экспорт\n", document);
        Assert.Equal(200, document.Length);
        // В модель уходит текст запроса без распознанных фильтров — они стоят ноль и только шумят.
        Assert.Equal("падает экспорт", context.Reranker.LastQuery);
    }
}
