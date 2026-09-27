using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.ScreenSetCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.CustomFields;
using Flow.Shared.Contracts.Boards;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using DomainContext = Flow.Domain.Entities.ScreenContext;
using DomainFieldType = Flow.Domain.Entities.CustomFieldType;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Экраны и RequireFields на Postgres (этап 3C): поля экрана в jsonb и условие перехода в uuid[] переживают
/// перезагрузку, экран «для всех типов» один на контекст (unique с NULLS NOT DISTINCT).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ScreenPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Screens_And_Required_Fields_Round_Trip()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Экраны", "SCR"))).Response!;
        var withField = (await db.SendAsync(new CustomFieldCreateCommand(Owner, board.Id, "steps", "Шаги", DomainFieldType.Text)))!;
        var steps = withField.CustomFields![0].Id;

        await db.SendAsync(new ScreenSetCommand(Owner, board.Id, null, DomainContext.Detail,
            [new ScreenFieldDto("system:due", Section: "Сроки"), new ScreenFieldDto($"custom:{steps}", Required: true)]));
        var replaced = (await db.SendAsync(new ScreenSetCommand(Owner, board.Id, null, DomainContext.Detail,
            [new ScreenFieldDto($"custom:{steps}", Required: true), new ScreenFieldDto("system:due", Section: "Сроки")])))!;
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        await db.SendAsync(new WorkflowSetCommand(Owner, board.Id, WorkflowMode.Free,
            [new TransitionRequest(null, done, Conditions: new TransitionConditionsDto(RequireFields: [steps]))]));

        var screens = await db.QueryAsync(ctx => ctx.Set<Flow.Domain.Entities.TaskScreen>().AsNoTracking().Where(s => s.BoardId == board.Id).ToListAsync());
        var screen = Assert.Single(screens);
        Assert.Equal([$"custom:{steps}", "system:due"], screen.Fields.Select(f => f.Field));
        Assert.Equal("Сроки", screen.Fields[1].Section);
        Assert.Single(replaced.Screens!);

        var transition = await db.QueryAsync(ctx => ctx.Set<Flow.Domain.Entities.StatusTransition>().AsNoTracking().SingleAsync(t => t.BoardId == board.Id));
        Assert.Equal([steps], transition.RequireFields);

        // Второй экран «для всех типов» того же контекста БД не пустит, даже в обход домена.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync(
            $"""INSERT INTO "TaskScreens" ("Id", "BoardId", "TaskTypeId", "Context", "Fields") VALUES ({Guid.NewGuid()}, {board.Id}, NULL, 1, '[]')""")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }
}
