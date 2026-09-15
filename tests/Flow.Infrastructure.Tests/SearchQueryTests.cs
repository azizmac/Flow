using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Гибридная выдача на живом pgvector: обе половины, слияние RRF, свёртка чанков в источники,
/// фильтры, пагинация и деградация. Эмбеддер — фейк (мешок слов), поэтому «похожесть» здесь
/// предсказуема; настоящее качество модели проверяют тесты категории Model.
/// </summary>
[Collection(SearchCollection.Name)]
public class SearchQueryTests(SearchFixture fixture)
{
    private static readonly Guid Owner = SearchFixture.OwnerId;

    private static int _keySuffix;

    private async Task<BoardResponse> CreateBoardAsync(string name = "Поиск")
    {
        var key = $"QRY{Interlocked.Increment(ref _keySuffix)}";
        return (await fixture.SendAsync(new BoardCreateCommand(Owner, $"{name} {key}", key))).Response!;
    }

    private Task<TaskResponse?> CreateTaskAsync(Guid boardId, string title, string? description = null) =>
        fixture.SendAsync(new TaskCreateCommand(Owner, boardId, title, description, null));

    private Task<SearchResponse?> SearchAsync(
        string text,
        Guid? boardId = null,
        SearchMode mode = SearchMode.Hybrid,
        IReadOnlyList<SearchSourceType>? types = null,
        bool includeArchived = false,
        int limit = 20,
        int offset = 0) =>
        fixture.SendAsync(new SearchQuery(Owner, text, types, boardId, includeArchived, mode, limit, offset));

    [Fact]
    public async Task Text_Half_Finds_Task_And_Highlights_It()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id, "Падает экспорт отчёта в PDF", "При нажатии «Скачать» приходит ошибка 500.");
        await fixture.DrainIndexingAsync();

        var response = await SearchAsync("экспорт отчёта", board.Id, SearchMode.Text);

        var item = Assert.Single(response!.Items);
        Assert.Equal(SearchSourceType.Task, item.SourceType);
        Assert.Equal(task!.Id, item.SourceId);
        Assert.Equal(board.Id, item.BoardId);
        Assert.Equal("Падает экспорт отчёта в PDF", item.Title);
        Assert.Equal(task.Code, item.TaskCode);
        // Подсветка идёт по чистому Content — шапка чанка в БД не хранится.
        Assert.Contains("<mark>", item.Snippet);
        Assert.False(response.Degraded);
        Assert.Equal(SearchMode.Text, response.Mode);
    }

    [Fact]
    public async Task Hybrid_Scores_Higher_Than_One_Half_Alone()
    {
        var board = await CreateBoardAsync();
        await CreateTaskAsync(board.Id, "Падает выгрузка накладной", "Сервис отчётов отвечает ошибкой.");
        await fixture.DrainIndexingAsync();

        var text = await SearchAsync("падает выгрузка", board.Id, SearchMode.Text);
        var hybrid = await SearchAsync("падает выгрузка", board.Id);

        var textScore = Assert.Single(text!.Items).Score;
        var hybridScore = hybrid!.Items.First(item => item.SourceType == SearchSourceType.Task).Score;

        // RRF складывает обратные ранги: источник, найденный обеими половинами, получает их сумму.
        Assert.True(hybridScore > textScore, $"гибрид {hybridScore:F5} должен быть выше половины {textScore:F5}");
        Assert.Equal(SearchMode.Hybrid, hybrid.Mode);
    }

    [Fact]
    public async Task Semantic_Half_Works_Without_Text_Match()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id, "Каталог поставщиков", "Список контрагентов и договоров.");
        await fixture.DrainIndexingAsync();

        var semantic = await SearchAsync("каталог поставщиков", board.Id, SearchMode.Semantic);

        // Векторная половина всегда отдаёт топ-N ближайших, даже далёких: важно, кто первый.
        Assert.Equal(task!.Id, semantic!.Items[0].SourceId);
        Assert.Equal(SearchMode.Semantic, semantic.Mode);
    }

    [Fact]
    public async Task Many_Chunks_Of_One_Source_Collapse_Into_One_Result()
    {
        var board = await CreateBoardAsync();
        var paragraph = string.Join(' ', Enumerable.Repeat("Подробное описание регламента приёмки груза.", 30));
        var task = await CreateTaskAsync(board.Id, "Регламент приёмки", $"{paragraph}\n\n{paragraph}");
        await fixture.DrainIndexingAsync();

        var response = await SearchAsync("регламент приёмки груза", board.Id, SearchMode.Text);

        // У источника несколько чанков, но в выдаче он один — берётся лучший чанк.
        var item = Assert.Single(response!.Items);
        Assert.Equal(task!.Id, item.SourceId);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task Comment_Is_Found_And_Carries_Its_Task()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id, "Интеграция с 1С");
        await fixture.SendAsync(new TaskCommentAddCommand(Owner, task!.Id, "Воспроизводится только на проде при синхронизации остатков."));
        await fixture.DrainIndexingAsync();

        var response = await SearchAsync("синхронизация остатков", board.Id, SearchMode.Text);

        var item = Assert.Single(response!.Items, i => i.SourceType == SearchSourceType.Comment);
        // У комментария в выдаче — название, код и id его задачи: иначе результат некуда открыть.
        Assert.Equal("Интеграция с 1С", item.Title);
        Assert.Equal(task.Code, item.TaskCode);
        Assert.Equal(board.Id, item.BoardId);
        Assert.Equal(task.Id, item.ParentId);
    }

    [Fact]
    public async Task Types_Filter_Narrows_Results()
    {
        var board = await CreateBoardAsync("Логистика");
        var task = await CreateTaskAsync(board.Id, "Логистика складов");
        await fixture.DrainIndexingAsync();

        var onlyBoards = await SearchAsync("логистика", board.Id, SearchMode.Text, [SearchSourceType.Board]);
        var onlyTasks = await SearchAsync("логистика", board.Id, SearchMode.Text, [SearchSourceType.Task]);

        Assert.All(onlyBoards!.Items, item => Assert.Equal(SearchSourceType.Board, item.SourceType));
        Assert.Equal(board.Id, Assert.Single(onlyBoards.Items).SourceId);
        Assert.Equal(task!.Id, Assert.Single(onlyTasks!.Items).SourceId);
    }

    [Fact]
    public async Task Board_Filter_Keeps_Other_Projects_Out()
    {
        var first = await CreateBoardAsync();
        var second = await CreateBoardAsync();
        await CreateTaskAsync(first.Id, "Уникальное слово кракозябра здесь");
        await CreateTaskAsync(second.Id, "Уникальное слово кракозябра тоже");
        await fixture.DrainIndexingAsync();

        var response = await SearchAsync("кракозябра", first.Id, SearchMode.Text);

        Assert.All(response!.Items, item => Assert.Equal(first.Id, item.BoardId));
        Assert.Single(response.Items, item => item.SourceType == SearchSourceType.Task);
    }

    [Fact]
    public async Task Closed_Tasks_Are_Hidden_Until_Asked_For()
    {
        var board = await CreateBoardAsync();
        var task = await CreateTaskAsync(board.Id, "Архивная задача про дирижабли");
        var done = board.Statuses.First(s => s.IsFinal).Id;
        await fixture.SendAsync(new TaskUpdateCommand(Owner, task!.Id, null, null, done));
        await fixture.DrainIndexingAsync();

        var open = await SearchAsync("дирижабли", board.Id, SearchMode.Text);
        var archived = await SearchAsync("дирижабли", board.Id, SearchMode.Text, includeArchived: true);

        Assert.DoesNotContain(open!.Items, item => item.SourceId == task.Id);
        Assert.Contains(archived!.Items, item => item.SourceId == task.Id);
    }

    [Fact]
    public async Task Deactivated_People_Are_Not_Returned()
    {
        var user = (await fixture.SendAsync(new UserCreateCommand(
            Owner, "kraken.search", "kraken.search@example.com", "Кракен", "Поисковый", "correct horse battery", null))).Response!;
        await fixture.DrainIndexingAsync();

        var before = await SearchAsync("кракен", mode: SearchMode.Text, types: [SearchSourceType.User]);
        Assert.Contains(before!.Items, item => item.SourceId == user.Id);

        await fixture.SendAsync(new UserDeactivateCommand(Owner, user.Id));

        // Чанк остаётся до прохода воркера, но в выдаче ушедшего быть не должно уже сейчас.
        var after = await SearchAsync("кракен", mode: SearchMode.Text, types: [SearchSourceType.User]);
        Assert.DoesNotContain(after!.Items, item => item.SourceId == user.Id);
    }

    [Fact]
    public async Task Limit_And_Offset_Page_Through_Stable_Total()
    {
        var board = await CreateBoardAsync();
        for (var i = 1; i <= 3; i++)
            await CreateTaskAsync(board.Id, $"Пагинация номер {i} про антресоли");

        await fixture.DrainIndexingAsync();

        var first = await SearchAsync("антресоли", board.Id, SearchMode.Text, limit: 2);
        var second = await SearchAsync("антресоли", board.Id, SearchMode.Text, limit: 2, offset: 2);

        Assert.Equal(2, first!.Items.Count);
        Assert.Single(second!.Items);
        // Total не зависит от страницы: это размер всей найденной выдачи, а не текущего куска.
        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Empty(first.Items.Select(i => i.SourceId).Intersect(second.Items.Select(i => i.SourceId)));
    }

    [Fact]
    public async Task Unavailable_Embedder_Degrades_But_Still_Finds()
    {
        var board = await CreateBoardAsync();
        await CreateTaskAsync(board.Id, "Погашенная модель не ломает поиск");
        await fixture.DrainIndexingAsync();

        fixture.Embedder.Unavailable = true;
        try
        {
            var response = await SearchAsync("погашенная модель", board.Id);

            Assert.True(response!.Degraded);
            Assert.Equal(SearchMode.Text, response.Mode);
            Assert.NotEmpty(response.Items);
        }
        finally
        {
            fixture.Embedder.Unavailable = false;
        }
    }

    [Fact]
    public async Task Nothing_Found_Is_An_Empty_Page_Not_An_Error()
    {
        var board = await CreateBoardAsync();
        await CreateTaskAsync(board.Id, "Обычная задача");
        await fixture.DrainIndexingAsync();

        var response = await SearchAsync("абсолютнонесуществующееслово", board.Id, SearchMode.Text);

        Assert.Empty(response!.Items);
        Assert.Equal(0, response.Total);
    }
}
