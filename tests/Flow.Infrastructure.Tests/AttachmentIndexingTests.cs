using System.Text;
using Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Поиск по содержимому вложений (docs/TZ_attachments.md, этап 4): текст достаётся из файла
/// в хранилище, файл без текста индексируется именем, закрытость наследуется от задачи.
/// Хранилище — в памяти (см. SearchFixture), эмбеддер — фейк: проверяется индексация, а не S3 и не модель.
/// </summary>
[Collection(SearchCollection.Name)]
public class AttachmentIndexingTests(SearchFixture fixture)
{
    private static readonly Guid Owner = SearchFixture.OwnerId;

    /// <summary>Заголовок PNG: настоящей картинки не нужно — важно, что извлекать из неё нечего.</summary>
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static int _keySuffix;

    private async Task<(BoardResponse Board, TaskResponse Task)> CreateTaskAsync()
    {
        var key = $"ATIX{Interlocked.Increment(ref _keySuffix)}";
        var board = (await fixture.SendAsync(new BoardCreateCommand(Owner, $"Проект {key}", key))).Response!;
        var task = (await fixture.SendAsync(new TaskCreateCommand(Owner, board.Id, "Задача с документами", null, null)))!;
        return (board, task);
    }

    private async Task<Guid> UploadAsync(Guid taskId, string fileName, byte[] bytes)
    {
        var result = await fixture.SendAsync(new AttachmentUploadCommand(Owner, taskId, fileName, bytes.Length, new MemoryStream(bytes)));
        return result.Response!.Id;
    }

    private Task<Guid> UploadTextAsync(Guid taskId, string fileName, string text) =>
        UploadAsync(taskId, fileName, Encoding.UTF8.GetBytes(text));

    private Task<List<ChunkRow>> ChunksAsync(Guid attachmentId) =>
        fixture.QueryAsync(db => db.SearchChunks
            .Where(c => c.SourceType == SearchSourceType.Attachment && c.SourceId == attachmentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new ChunkRow(c.Content, c.IsClosed, c.BoardId))
            .ToListAsync());

    [Fact]
    public async Task Text_Of_The_File_Gets_Into_The_Index()
    {
        var (board, task) = await CreateTaskAsync();
        var attachmentId = await UploadTextAsync(task.Id, "смета на ремонт.txt", "Смета на замену компрессора холодильной камеры, срок поставки шесть недель.");

        await fixture.DrainIndexingAsync();

        var chunk = Assert.Single(await ChunksAsync(attachmentId));
        Assert.Contains("компрессора холодильной камеры", chunk.Content);
        // Имя файла — первой строкой содержимого: по нему ищут не реже, чем по тексту внутри,
        // а полнотекстовая ветка смотрит только в Content.
        Assert.StartsWith("смета на ремонт.txt", chunk.Content);
        Assert.Equal(board.Id, chunk.BoardId);
        Assert.False(chunk.IsClosed);
    }

    [Fact]
    public async Task File_Without_Text_Is_Indexed_By_Its_Name()
    {
        var (_, task) = await CreateTaskAsync();
        var attachmentId = await UploadAsync(task.Id, "скан договора.png", Png);

        await fixture.DrainIndexingAsync();

        // Скан, картинка, битый файл: содержимого нет, но вложение всё равно должно находиться по имени.
        var chunk = Assert.Single(await ChunksAsync(attachmentId));
        Assert.Equal("скан договора.png", chunk.Content);
    }

    [Fact]
    public async Task Closed_Task_Closes_Its_Attachments()
    {
        var (board, task) = await CreateTaskAsync();
        var attachmentId = await UploadTextAsync(task.Id, "протокол.txt", "Протокол встречи по интеграции с 1С.");
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new TaskUpdateCommand(Owner, task.Id, null, null, board.Statuses.First(s => s.IsFinal).Id));
        await fixture.SendAsync(new ReindexCommand(Owner, [SearchSourceType.Attachment], board.Id));
        await fixture.DrainIndexingAsync();

        // Иначе фильтр «без архива» врал бы: задача в архиве, а её файлы продолжали бы всплывать.
        var chunk = Assert.Single(await ChunksAsync(attachmentId));
        Assert.True(chunk.IsClosed);
    }

    [Fact]
    public async Task Attachment_Is_Found_By_Text_Inside_The_File()
    {
        var (board, task) = await CreateTaskAsync();
        await UploadTextAsync(task.Id, "требования.txt", "Экспорт остатков в 1С выполняется ночным заданием по расписанию.");

        await fixture.DrainIndexingAsync();

        var response = await fixture.SendAsync(new SearchQuery(
            Owner, "ночное задание экспорта остатков", [SearchSourceType.Attachment], board.Id, IncludeArchived: true, SearchMode.Text, 10, 0));

        // Режим Text: фейковый эмбеддер не про смысл, а полнотекстовая ветка честно ищет по содержимому файла.
        var item = Assert.Single(response!.Items);
        Assert.Equal(SearchSourceType.Attachment, item.SourceType);
        Assert.Equal(task.Code, item.TaskCode);
    }

    [Fact]
    public async Task Deleting_Attachment_Removes_Its_Chunks()
    {
        var (_, task) = await CreateTaskAsync();
        var attachmentId = await UploadTextAsync(task.Id, "старый прайс.txt", "Прайс на комплектующие, редакция от января.");
        await fixture.DrainIndexingAsync();
        Assert.NotEmpty(await ChunksAsync(attachmentId));

        await fixture.SendAsync(new AttachmentDeleteCommand(Owner, attachmentId));
        await fixture.DrainIndexingAsync();

        Assert.Empty(await ChunksAsync(attachmentId));
    }

    [Fact]
    public async Task Deleting_Task_Removes_Chunks_Of_Its_Attachments()
    {
        var (_, task) = await CreateTaskAsync();
        var first = await UploadTextAsync(task.Id, "первый.txt", "Первый документ задачи.");
        var second = await UploadTextAsync(task.Id, "второй.txt", "Второй документ задачи.");
        await fixture.DrainIndexingAsync();

        await fixture.SendAsync(new TaskDeleteCommand(Owner, task.Id));
        await fixture.DrainIndexingAsync();

        // Строки уносит каскад БД, а чанки привязаны к Id вложений — список собирается до удаления.
        Assert.Empty(await ChunksAsync(first));
        Assert.Empty(await ChunksAsync(second));
    }

    [Fact]
    public async Task Reindex_Picks_Up_Files_Uploaded_Earlier()
    {
        var (board, task) = await CreateTaskAsync();
        var attachmentId = await UploadTextAsync(task.Id, "инструкция.txt", "Инструкция по настройке шлюза оплаты.");
        await fixture.DrainIndexingAsync();

        await fixture.QueryAsync(db => db.SearchChunks
            .Where(c => c.SourceType == SearchSourceType.Attachment && c.SourceId == attachmentId)
            .ExecuteDeleteAsync());

        // Так наполняют индекс по файлам, загруженным до включения поиска по вложениям.
        var enqueued = await fixture.SendAsync(new ReindexCommand(Owner, [SearchSourceType.Attachment], board.Id));
        await fixture.DrainIndexingAsync();

        Assert.Equal(1, enqueued);
        Assert.NotEmpty(await ChunksAsync(attachmentId));
    }

    private sealed record ChunkRow(string Content, bool IsClosed, Guid? BoardId);
}
