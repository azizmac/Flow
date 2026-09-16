using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Фильтры из строки запроса доходят до индекса: @username и «мои» → исполнитель, «просроченные»,
/// «проект:», «статус:», «за неделю», код задачи → прямое попадание. Нераспознанное возвращается
/// в текст: опечатка в фильтре не должна молча обнулять выдачу.
/// </summary>
public class SearchIntentFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(SearchTestContext context, string name = "Бэкенд", string key = "DBACK")
    {
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, name, key), CancellationToken.None)).Response!;
        context.Tasks.RegisterBoardStatuses((await context.Boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return board;
    }

    private static Guid AddUser(FakeUserRepository users, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    private static SearchQuery Query(string text) =>
        new(Owner, text, null, null, IncludeArchived: false, SearchMode.Hybrid, 20, 0);

    [Fact]
    public async Task Assignee_Narrows_Search_To_Tasks()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var assignee = AddUser(context.Users, "ivanov");

        var response = await context.Mediator.Send(Query("экспорт @ivanov"), CancellationToken.None);

        Assert.Equal(assignee, context.Index.LastCriteria!.AssigneeId);
        // Исполнитель есть только у задач: проект или человек «назначенными» не бывают.
        Assert.Equal([SearchSourceType.Task], context.Index.LastCriteria.Types.ToArray());
        Assert.Equal("экспорт", context.Index.LastCriteria.Query);
        Assert.Contains("@ivanov", response!.Intent.Filters);
    }

    [Fact]
    public async Task Unknown_Assignee_Goes_Back_Into_Text()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        var response = await context.Mediator.Send(Query("экспорт @nobody"), CancellationToken.None);

        Assert.Null(context.Index.LastCriteria!.AssigneeId);
        Assert.Equal("экспорт @nobody", context.Index.LastCriteria.Query);
        Assert.Empty(response!.Intent.Filters);
    }

    [Fact]
    public async Task Mine_Resolves_To_Current_User()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        var response = await context.Mediator.Send(Query("мои задачи про экспорт"), CancellationToken.None);

        Assert.Equal(Owner, context.Index.LastCriteria!.AssigneeId);
        Assert.Contains("мои", response!.Intent.Filters);
    }

    [Fact]
    public async Task Overdue_Sets_The_Flag()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        var response = await context.Mediator.Send(Query("просроченные экспорт"), CancellationToken.None);

        Assert.True(context.Index.LastCriteria!.OverdueOnly);
        Assert.Contains("просроченные", response!.Intent.Filters);
    }

    [Fact]
    public async Task Board_Filter_Resolves_Key_To_Id()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var board = await CreateBoardAsync(context);

        var response = await context.Mediator.Send(Query("экспорт проект:dback"), CancellationToken.None);

        Assert.Equal(board.Id, context.Index.LastCriteria!.BoardId);
        Assert.Contains("проект DBACK", response!.Intent.Filters);
    }

    [Fact]
    public async Task Status_Filter_Resolves_Name_To_Ids()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var board = await CreateBoardAsync(context);
        var inProgress = board.Statuses.First(status => !status.IsInitial && !status.IsFinal);

        var response = await context.Mediator.Send(Query($"экспорт статус:{inProgress.Name}"), CancellationToken.None);

        Assert.Contains(inProgress.Id, context.Index.LastCriteria!.StatusIds!);
        Assert.Contains("статус: " + inProgress.Name, response!.Intent.Filters);
    }

    [Fact]
    public async Task Unknown_Status_Goes_Back_Into_Text()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        await CreateBoardAsync(context);

        await context.Mediator.Send(Query("экспорт статус:абракадабра"), CancellationToken.None);

        Assert.Empty(context.Index.LastCriteria!.StatusIds ?? []);
        Assert.Equal("экспорт абракадабра", context.Index.LastCriteria.Query);
    }

    [Fact]
    public async Task Period_Becomes_Updated_Since()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        var response = await context.Mediator.Send(Query("экспорт за неделю"), CancellationToken.None);

        var since = Assert.IsType<DateTime>(context.Index.LastCriteria!.UpdatedSince);
        Assert.InRange(since, DateTime.UtcNow.AddDays(-7).AddMinutes(-1), DateTime.UtcNow.AddDays(-7).AddMinutes(1));
        Assert.Contains("за неделю", response!.Intent.Filters);
    }

    [Fact]
    public async Task Task_Code_Puts_The_Task_First_And_Skips_The_Search()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var board = await CreateBoardAsync(context);
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Падает экспорт", null, null), CancellationToken.None))!;

        var response = await context.Mediator.Send(Query(task.Code), CancellationToken.None);

        // Код в строке — не поиск, а прямое попадание: в индекс за этим ходить незачем.
        Assert.Null(context.Index.LastCriteria);
        var item = Assert.Single(response!.Items);
        Assert.Equal(task.Id, item.SourceId);
        Assert.Equal(task.Id, response.Intent.TaskId);
        Assert.Contains(task.Code, response.Intent.Filters);
        Assert.Equal(SearchMode.Filters, response.Mode);
    }

    [Fact]
    public async Task Task_Code_With_Text_Keeps_Searching_And_Pins_The_Task()
    {
        var context = TestMediatorFactory.CreateSearchContext();
        var board = await CreateBoardAsync(context);
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Падает экспорт", null, null), CancellationToken.None))!;
        context.Index.Page = new Flow.Application.Abstractions.SearchPage(
            [new Flow.Application.Abstractions.SearchHit(SearchSourceType.Comment, Guid.NewGuid(), board.Id, "Другая", "фрагмент", 0.01, "DBACK-9", DateTime.UtcNow, null)],
            Total: 1);

        var response = await context.Mediator.Send(Query($"{task.Code} экспорт"), CancellationToken.None);

        Assert.Equal("экспорт", context.Index.LastCriteria!.Query);
        Assert.Equal(task.Id, response!.Items[0].SourceId);
        Assert.Equal(2, response.Total);
    }

    [Fact]
    public async Task Unknown_Task_Code_Goes_Back_Into_Text()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        await context.Mediator.Send(Query("NOPE-77 экспорт"), CancellationToken.None);

        Assert.Equal("экспорт NOPE-77", context.Index.LastCriteria!.Query);
    }

    [Fact]
    public async Task Filters_Without_Text_Do_Not_Call_The_Model()
    {
        var context = TestMediatorFactory.CreateSearchContext();

        var response = await context.Mediator.Send(Query("мои просроченные"), CancellationToken.None);

        // Эмбеддить нечего: выдача отбирается фильтрами и сортируется по дате.
        Assert.Equal(0, context.Embeddings.Calls);
        Assert.Null(context.Index.LastCriteria!.QueryEmbedding);
        Assert.False(context.Index.LastCriteria.UseText);
        Assert.Equal(SearchMode.Filters, response!.Mode);
        Assert.Equal("", response.Intent.Text);
    }
}
