using System.Text;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Слияние, разделение и перенос на реальном Postgres (этап 1E): прежний код находит задачу через TaskCodeAliases,
/// удаление задачи уносит алиасы каскадом, у переехавшего вложения новый ключ и проект, связи слияния не
/// нарушают unique (Source, Target, Type), части разделения получают соседние ранги.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskRestructurePersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Move_Keeps_The_Old_Code_Findable_And_Relocates_Files()
    {
        var source = (await db.SendAsync(new BoardCreateCommand(Owner, "Откуда", "MVSRC"))).Response!;
        var target = (await db.SendAsync(new BoardCreateCommand(Owner, "Куда", "MVDST"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, source.Id, "Переезжает", null, null)))!;
        var bytes = Encoding.UTF8.GetBytes("план");
        await db.SendAsync(new AttachmentUploadCommand(Owner, task.Id, "план.txt", bytes.Length, new MemoryStream(bytes)));

        var moved = (await db.SendAsync(new TaskMoveCommand(Owner, task.Id, target.Id)))!;

        Assert.Equal("MVDST-1", moved.Code);
        Assert.Equal(task.Id, (await db.SendAsync(new TaskGetByCodeQuery(Owner, "mvsrc-1")))!.Id);
        var attachment = await db.QueryAsync(ctx => ctx.Attachments.AsNoTracking().SingleAsync(a => a.TaskId == task.Id));
        Assert.Equal(target.Id, attachment.BoardId);
        Assert.StartsWith(Attachment.TaskPrefix(target.Id, task.Id), attachment.StorageKey);
        Assert.True(db.Storage.Objects.ContainsKey(attachment.StorageKey));

        await db.SendAsync(new TaskDeleteCommand(Owner, task.Id));
        Assert.False(await db.QueryAsync(ctx => ctx.TaskCodeAliases.AnyAsync(a => a.TaskId == task.Id)));
    }

    [Fact]
    public async Task Merge_And_Split_Persist_Links_Comments_And_Ranks()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Слияние", "MRGP"))).Response!;
        async Task<TaskResponse> Task(string title) => (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, null)))!;
        var source = await Task("Дубль");
        var target = await Task("Основная");
        var other = await Task("Третья");
        await db.SendAsync(new TaskLinkCreateCommand(Owner, source.Id, Flow.Domain.Entities.TaskLinkType.RelatesTo, other.Id));
        await db.SendAsync(new TaskLinkCreateCommand(Owner, target.Id, Flow.Domain.Entities.TaskLinkType.RelatesTo, other.Id));
        await db.SendAsync(new TaskCommentAddCommand(Owner, source.Id, "Контекст"));

        await db.SendAsync(new TaskMergeCommand(Owner, source.Id, target.Id));

        var links = await db.QueryAsync(ctx => ctx.TaskLinks.AsNoTracking()
            .Where(l => l.SourceTaskId == source.Id || l.TargetTaskId == source.Id || l.SourceTaskId == target.Id || l.TargetTaskId == target.Id).ToListAsync());
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Type == Flow.Domain.Entities.TaskLinkType.Duplicates && l.SourceTaskId == source.Id && l.TargetTaskId == target.Id);
        Assert.Equal(target.Id, await db.QueryAsync(ctx => ctx.TaskComments.Where(c => c.Body.Contains("Контекст")).Select(c => c.TaskId).SingleAsync()));

        var parts = (await db.SendAsync(new TaskSplitCommand(Owner, target.Id, [new SplitPart("А"), new SplitPart("Б")])))!;
        var ranks = await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.BoardId == board.Id).OrderBy(t => t.Rank).Select(t => t.Title).ToListAsync());
        Assert.Equal(["А", "Б"], ranks.TakeLast(2));
        Assert.Equal(2, parts.Count);
    }
}
