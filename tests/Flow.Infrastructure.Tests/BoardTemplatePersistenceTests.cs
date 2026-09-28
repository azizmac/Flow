using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Templates;
using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Flow.Shared.Contracts.Boards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Шаблоны проектов и перенос конфигурации на Postgres (этап 3F): проект по шаблону сохраняется целиком (статусы,
/// переходы, поля, экраны), чертёж лежит в jsonb, а перенос переименовывает статусы по цепочке и по кругу — unique
/// (BoardId, Name) не должен мешать ни обмену имён, ни удалению статуса с задачами.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BoardTemplatePersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Project_From_Bug_Tracker_Template_Round_Trips()
    {
        var created = (await db.SendAsync(new BoardCreateCommand(Owner, "Баги", "TPLBUG", BuiltInBoardTemplates.BugTrackerId))).Response!;

        var board = (await db.SendAsync(new BoardGetQuery(Owner, created.Id)))!;
        Assert.Equal(["Новая", "Подтверждена", "В работе", "Исправлена", "Проверена", "Отклонена"], board.Statuses.Select(s => s.Name));
        Assert.Equal(Flow.Shared.Contracts.Boards.WorkflowMode.Restricted, board.WorkflowMode);
        Assert.Equal(["environment", "steps", "severity"], board.CustomFields.Select(f => f.Key));
        var severity = board.CustomFields.Single(f => f.Key == "severity").Id;
        Assert.Contains(board.Screens!.Single().Fields, f => f.Field == $"custom:{severity}");
        var transitions = await db.QueryAsync(ctx => ctx.Set<StatusTransition>().AsNoTracking().CountAsync(t => t.BoardId == created.Id));
        Assert.Equal(6, transitions);
    }

    [Fact]
    public async Task Saved_Template_Lives_In_Jsonb_And_Creates_Project_With_Samples()
    {
        var source = (await db.SendAsync(new BoardCreateCommand(Owner, "Исходный", "TPLSRC", BuiltInBoardTemplates.KanbanId))).Response!;
        await db.SendAsync(new TaskCreateCommand(Owner, source.Id, "Образец", "Текст", null));

        var template = (await db.SendAsync(new BoardTemplateSaveCommand(Owner, source.Id, "Kanban команды", null, IncludeTasks: true)))!;
        var stored = await db.QueryAsync(ctx => ctx.Database
            .SqlQuery<string>($"""SELECT "Payload"->'statuses'->2->>'name' AS "Value" FROM "BoardTemplates" WHERE "Id" = {template.Id}""").SingleAsync());
        Assert.Equal("В работе", stored);

        var copy = (await db.SendAsync(new BoardCreateCommand(Owner, "Копия", "TPLCPY", template.Id))).Response!;
        Assert.Equal(1, copy.TaskCount);
        Assert.Equal(5, copy.Statuses.Single(s => s.Name == "В работе").WipLimit);
        var titles = await db.QueryAsync(ctx => ctx.Set<TaskItem>().AsNoTracking().Where(t => t.BoardId == copy.Id).Select(t => t.Title).ToListAsync());
        Assert.Equal(["Образец"], titles);
    }

    [Fact]
    public async Task Apply_Swaps_Names_Chains_Renames_And_Removes_Status_With_Tasks()
    {
        var source = (await db.SendAsync(new BoardCreateCommand(Owner, "Источник", "TPLAPS"))).Response!;
        var target = (await db.SendAsync(new BoardCreateCommand(Owner, "Цель", "TPLAPT"))).Response!;
        var extra = (await db.SendAsync(new Flow.Application.Features.Boards.Commands.StatusCreateCommand.StatusCreateCommand(Owner, target.Id, "Ожидание", null)))!
            .Statuses.Single(s => s.Name == "Ожидание").Id;
        var waiting = (await db.SendAsync(new TaskCreateCommand(Owner, target.Id, "Ждёт", null, extra)))!;

        Guid S(BoardResponse b, string name) => b.Statuses.Single(s => s.Name == name).Id;
        // Крест-накрест: «В работе» цели становится «На проверке» источника и наоборот — имена меняются по кругу.
        var map = new Dictionary<Guid, Guid>
        {
            [S(target, "В работе")] = S(source, "На проверке"),
            [S(target, "На проверке")] = S(source, "В работе"),
            [extra] = S(source, "Сделана")
        };

        var result = (await db.SendAsync(new BoardApplyConfigCommand(Owner, source.Id, [new ApplyConfigTarget(target.Id, map)], ConfigParts.All)))!;

        var applied = Assert.Single(result.Results);
        Assert.True(applied.Success, applied.Error);
        var after = (await db.SendAsync(new BoardGetQuery(Owner, target.Id)))!;
        Assert.Equal(["Не начата", "В работе", "На проверке", "Сделана"], after.Statuses.Select(s => s.Name));
        Assert.Equal(S(target, "На проверке"), S(after, "В работе"));
        var moved = await db.QueryAsync(ctx => ctx.Set<TaskItem>().AsNoTracking().SingleAsync(t => t.Id == waiting.Id));
        Assert.Equal(S(target, "Сделана"), moved.StatusId);

        var again = (await db.SendAsync(new BoardApplyConfigCommand(Owner, source.Id, [new ApplyConfigTarget(target.Id)], ConfigParts.All)))!;
        Assert.False(again.Results.Single().Changed);
    }
}
