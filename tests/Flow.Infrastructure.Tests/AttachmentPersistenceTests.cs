using System.Text;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Вложения в Postgres: каскад при удалении задачи и проекта и уникальность файла внутри задачи
/// на уровне БД, а не только в хендлере.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AttachmentPersistenceTests(PostgresFixture fixture)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private static int _keySuffix;

    private async Task<(Guid BoardId, TaskResponse Task)> CreateTaskAsync()
    {
        var key = $"ATP{Interlocked.Increment(ref _keySuffix)}";
        var board = (await fixture.SendAsync(new BoardCreateCommand(Owner, $"Вложения {key}", key))).Response!;
        var task = (await fixture.SendAsync(new TaskCreateCommand(Owner, board.Id, "Задача с файлом", null, null)))!;
        return (board.Id, task);
    }

    private Task<Flow.Application.Features.Attachments.AttachmentUploadResult> UploadAsync(Guid taskId, string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return fixture.SendAsync(new AttachmentUploadCommand(Owner, taskId, fileName, bytes.Length, new MemoryStream(bytes)));
    }

    [Fact]
    public async Task Attachment_Is_Stored_With_Task_And_Board()
    {
        var (boardId, task) = await CreateTaskAsync();

        var uploaded = await UploadAsync(task.Id, "смета.xlsx", "содержимое сметы");

        var stored = await fixture.QueryAsync(db => db.Attachments.FirstOrDefaultAsync(a => a.Id == uploaded.Response!.Id));
        Assert.NotNull(stored);
        Assert.Equal(task.Id, stored!.TaskId);
        // BoardId дублируется из задачи: по нему строится ключ объекта и фильтруется поиск.
        Assert.Equal(boardId, stored.BoardId);
        Assert.Equal("смета.xlsx", stored.FileName);
        Assert.True(fixture.Storage.Objects.ContainsKey(stored.StorageKey));
    }

    [Fact]
    public async Task Deleting_Task_Cascades_To_Attachments()
    {
        var (boardId, task) = await CreateTaskAsync();
        await UploadAsync(task.Id, "первый.txt", "1");
        await UploadAsync(task.Id, "второй.txt", "2");

        await fixture.SendAsync(new TaskDeleteCommand(Owner, task.Id));

        var left = await fixture.QueryAsync(db => db.Attachments.CountAsync(a => a.TaskId == task.Id));
        Assert.Equal(0, left);
        // Каскад БД уносит строки, объекты удаляет хендлер — иначе файлы пережили бы свою задачу.
        Assert.DoesNotContain(fixture.Storage.Objects.Keys, key => key.StartsWith($"attachments/{boardId}/{task.Id}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_Board_Cascades_To_Attachments()
    {
        var (boardId, task) = await CreateTaskAsync();
        await UploadAsync(task.Id, "файл.txt", "содержимое");

        await fixture.SendAsync(new BoardDeleteCommand(Owner, boardId));

        var left = await fixture.QueryAsync(db => db.Attachments.CountAsync(a => a.BoardId == boardId));
        Assert.Equal(0, left);
        Assert.DoesNotContain(fixture.Storage.Objects.Keys, key => key.StartsWith($"attachments/{boardId}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Same_Content_Twice_In_One_Task_Breaks_On_The_Unique_Index()
    {
        var (boardId, task) = await CreateTaskAsync();
        var uploaded = await UploadAsync(task.Id, "договор.pdf", "текст договора");
        var stored = await fixture.QueryAsync(db => db.Attachments.FirstAsync(a => a.Id == uploaded.Response!.Id));

        // Хендлер такой файл не пропустит, но защита должна быть и на уровне БД: гонка двух загрузок
        // иначе оставила бы два одинаковых вложения.
        var duplicate = Attachment.Create(task.Id, boardId, "копия.pdf", "application/pdf", 10, stored.ContentHash, Owner);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            db.Attachments.Add(duplicate);
            await db.SaveChangesAsync();
            return true;
        }));
    }
}
