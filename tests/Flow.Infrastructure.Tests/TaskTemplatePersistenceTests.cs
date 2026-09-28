using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.TaskTemplates;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Шаблоны задач на Postgres (этап 3G): чек-лист в text[], подзадачи в jsonb переживают перезагрузку; задача по шаблону
/// с подзадачами вставляется одной транзакцией (родитель раньше детей, ранги подряд); удаление проекта уносит шаблоны.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskTemplatePersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Template_Round_Trips_Creates_Subtasks_And_Goes_With_Project()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Шаблоны задач", "TTPL"))).Response!;
        var story = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Story).Id;
        var created = (await db.SendAsync(new TaskTemplateCreateCommand(Owner, board.Id, new SaveTaskTemplateRequest(
            "Фича", "Фича {n}", story, Checklist: ["Макет", "Код"], Subtasks: [new("Бэкенд", null, ["API"]), new("Фронтенд")]))))!;

        var stored = await db.QueryAsync(ctx => ctx.Set<TaskTemplate>().AsNoTracking().SingleAsync(t => t.Id == created.Id));
        Assert.Equal(["Макет", "Код"], stored.Checklist);
        Assert.Equal(["API"], stored.Subtasks[0].Checklist);

        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, created.RenderedTitle, null, null, story, TemplateId: created.Id)))!;
        Assert.Equal("Фича 1", task.Title);
        var children = await db.QueryAsync(ctx => ctx.Set<TaskItem>().AsNoTracking().Where(t => t.ParentId == task.Id).OrderBy(t => t.Rank).Select(t => t.Title).ToListAsync());
        Assert.Equal(["Бэкенд", "Фронтенд"], children);
        Assert.Equal(1, await db.QueryAsync(ctx => ctx.Set<TaskTemplate>().Where(t => t.Id == created.Id).Select(t => t.UsageCount).SingleAsync()));

        await db.SendAsync(new BoardDeleteCommand(Owner, board.Id));
        Assert.False(await db.QueryAsync(ctx => ctx.Set<TaskTemplate>().AnyAsync(t => t.Id == created.Id)));
    }
}
