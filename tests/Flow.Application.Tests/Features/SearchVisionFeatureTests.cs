using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Tests.Fakes;
using Flow.Shared.Contracts.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Визуальная половина поиска (docs/TZ_search_vector.md, «Мультимодальность»): когда запрос уходит
/// в модель картинок и что происходит, если она молчит. Вектор фейковый — качество меряется руками
/// на реальных скриншотах, здесь важны решения хендлера.
/// </summary>
public class SearchVisionFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static SearchQuery Query(
        SearchMode mode = SearchMode.Hybrid,
        IReadOnlyList<SearchSourceType>? types = null) =>
        new(Owner, "скриншот ошибки оплаты", types, null, IncludeArchived: false, mode, 20, 0);

    private static SearchTestContext Ready(bool configured = true)
    {
        var context = TestMediatorFactory.CreateSearchContext();
        context.Vision.IsConfigured = configured;
        return context;
    }

    [Fact]
    public async Task Query_Goes_To_The_Image_Model_Too()
    {
        var context = Ready();

        await context.Mediator.Send(Query(), CancellationToken.None);

        Assert.Equal(1, context.Vision.QueryCalls);
        Assert.Equal("скриншот ошибки оплаты", context.Vision.LastQuery);
        // Обе половины доезжают до индекса: текстовая ищет среди текстов, визуальная среди картинок.
        Assert.NotNull(context.Index.LastCriteria!.QueryEmbedding);
        Assert.NotNull(context.Index.LastCriteria.VisionQueryEmbedding);
    }

    [Fact]
    public async Task Without_The_Service_There_Is_No_Second_Inference()
    {
        var context = Ready(configured: false);

        await context.Mediator.Send(Query(), CancellationToken.None);

        Assert.Equal(0, context.Vision.QueryCalls);
        Assert.Null(context.Index.LastCriteria!.VisionQueryEmbedding);
    }

    [Fact]
    public async Task Text_Mode_Does_Not_Touch_The_Image_Model()
    {
        var context = Ready();

        await context.Mediator.Send(Query(mode: SearchMode.Text), CancellationToken.None);

        // Режим «только слова» — это отладка и деградация: ни одна модель в нём не зовётся.
        Assert.Equal(0, context.Vision.QueryCalls);
        Assert.Null(context.Index.LastCriteria!.VisionQueryEmbedding);
    }

    [Fact]
    public async Task Search_Without_Attachments_Skips_The_Image_Half()
    {
        var context = Ready();

        await context.Mediator.Send(Query(types: [SearchSourceType.Task, SearchSourceType.Comment]), CancellationToken.None);

        // Визуальные чанки есть только у вложений: при фильтре «Задачи» второй инференс — деньги на ветер.
        Assert.Equal(0, context.Vision.QueryCalls);
        Assert.Null(context.Index.LastCriteria!.VisionQueryEmbedding);
    }

    [Fact]
    public async Task Silent_Image_Model_Leaves_The_Text_Search_Working()
    {
        var context = Ready();
        context.Vision.Fails = true;

        var response = await context.Mediator.Send(Query(), CancellationToken.None);

        // Выпадает только визуальная половина: выдача по текстам остаётся, ошибки пользователь не видит.
        Assert.NotNull(response);
        Assert.Equal(1, context.Vision.QueryCalls);
        Assert.NotNull(context.Index.LastCriteria!.QueryEmbedding);
        Assert.Null(context.Index.LastCriteria.VisionQueryEmbedding);
    }
}
