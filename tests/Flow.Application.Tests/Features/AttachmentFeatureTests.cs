using System.Text;
using Flow.Application.Exceptions;
using Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using Xunit;
using TaskActivityType = Flow.Domain.Entities.TaskActivityType;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Вложения задачи (docs/TZ_attachments.md): лимиты, запрещённые типы, дубли, права, журнал и порядок
/// «сначала объект, потом строка». Хранилище — в памяти: проверяется поведение хендлеров, а не S3.
/// </summary>
public class AttachmentFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];

    private static async Task<TaskResponse> CreateTaskAsync(AttachmentTestContext context)
    {
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!;
        context.Tasks.RegisterBoardStatuses((await context.Boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Задача", null, null), CancellationToken.None))!;
    }

    private static Task<Flow.Application.Features.Attachments.AttachmentUploadResult> UploadAsync(
        AttachmentTestContext context,
        Guid taskId,
        string fileName,
        byte[]? bytes = null,
        long? size = null,
        Guid? actorId = null)
    {
        var content = bytes ?? Encoding.UTF8.GetBytes("содержимое файла");
        return context.Mediator.Send(
            new AttachmentUploadCommand(actorId ?? Owner, taskId, fileName, size ?? content.Length, new MemoryStream(content)),
            CancellationToken.None);
    }

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    [Fact]
    public async Task Upload_Stores_Object_Row_And_Journal_Entry()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        var result = await UploadAsync(context, task.Id, "макет.pdf");

        var response = result.Response!;
        Assert.Equal("макет.pdf", response.FileName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.False(response.IsImage);

        var attachment = Assert.Single(context.Attachments.All);
        Assert.Equal(task.Id, attachment.TaskId);
        // Ключ строится из проекта и задачи, имя файла в него не попадает.
        Assert.StartsWith($"attachments/{attachment.BoardId}/{task.Id}/{attachment.Id}", attachment.StorageKey);
        Assert.EndsWith(".pdf", attachment.StorageKey);
        Assert.True(context.Storage.Objects.ContainsKey(attachment.StorageKey));

        var entry = Assert.Single(context.Activities.ForTask(task.Id), a => a.Type == TaskActivityType.AttachmentAdded);
        // Имя хранится в журнале: запись переживёт удаление файла.
        Assert.Equal("макет.pdf", entry.NewValue);
        Assert.Equal(attachment.Id.ToString(), entry.OldValue);
    }

    [Fact]
    public async Task Png_Is_Marked_As_Image()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        var result = await UploadAsync(context, task.Id, "снимок.png", Png);

        Assert.True(result.Response!.IsImage);
        Assert.Equal("image/png", result.Response.ContentType);
    }

    [Fact]
    public async Task File_Pretending_To_Be_An_Image_Loses_Inline()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        var result = await UploadAsync(context, task.Id, "ловушка.png", html);

        // Файл сохранён, но показывать его в браузере мы уже не будем: сигнатура не совпала.
        Assert.Equal("application/octet-stream", result.Response!.ContentType);
        Assert.False(result.Response.IsImage);
    }

    [Fact]
    public async Task Svg_Is_Never_Inline()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        var result = await UploadAsync(context, task.Id, "схема.svg", Encoding.UTF8.GetBytes("<svg/>"));

        // SVG — документ со скриптами, отдаётся он с нашего origin: тип сохраняем, inline не даём.
        Assert.Equal("image/svg+xml", result.Response!.ContentType);
        Assert.False(result.Response.IsImage);
    }

    [Fact]
    public async Task Too_Large_File_Is_Rejected_Before_Storage()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        var result = await UploadAsync(context, task.Id, "видео.mp4", size: context.Options.MaxFileBytes + 1);

        Assert.True(result.IsInvalid);
        Assert.Empty(context.Storage.Objects);
        Assert.Empty(context.Attachments.All);
    }

    [Fact]
    public async Task Blocked_Extension_Is_Rejected()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        var result = await UploadAsync(context, task.Id, "вирус.exe");

        Assert.True(result.IsInvalid);
        Assert.Contains(".exe", result.Error);
    }

    [Fact]
    public async Task Same_File_Twice_In_One_Task_Is_A_Conflict()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var first = await UploadAsync(context, task.Id, "договор.pdf");

        var second = await UploadAsync(context, task.Id, "договор-копия.pdf");

        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Response!.Id, second.DuplicateId);
        Assert.Single(context.Attachments.All);
    }

    [Fact]
    public async Task Same_File_In_Another_Task_Is_Allowed()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var first = await CreateTaskAsync(context);
        var second = (await context.Mediator.Send(
            new TaskCreateCommand(Owner, first.BoardId, "Вторая", null, null), CancellationToken.None))!;

        await UploadAsync(context, first.Id, "договор.pdf");
        var result = await UploadAsync(context, second.Id, "договор.pdf");

        // Между задачами файлы не переиспользуются: счётчик ссылок стоил бы дороже экономии.
        Assert.False(result.IsDuplicate);
        Assert.Equal(2, context.Attachments.All.Count);
    }

    [Fact]
    public async Task Count_Limit_Per_Task_Is_Enforced()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        context.Options.MaxPerTask = 2;
        var task = await CreateTaskAsync(context);

        await UploadAsync(context, task.Id, "первый.txt", Encoding.UTF8.GetBytes("1"));
        await UploadAsync(context, task.Id, "второй.txt", Encoding.UTF8.GetBytes("2"));
        var third = await UploadAsync(context, task.Id, "третий.txt", Encoding.UTF8.GetBytes("3"));

        Assert.True(third.IsInvalid);
    }

    [Fact]
    public async Task Total_Size_Limit_Per_Task_Is_Enforced()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        context.Options.MaxTotalBytesPerTask = 10;
        var task = await CreateTaskAsync(context);

        await UploadAsync(context, task.Id, "первый.txt", Encoding.UTF8.GetBytes("12345"));
        var second = await UploadAsync(context, task.Id, "второй.txt", Encoding.UTF8.GetBytes("1234567890"));

        Assert.True(second.IsInvalid);
    }

    [Fact]
    public async Task Upload_To_Unknown_Task_Is_Not_Found()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        var result = await UploadAsync(context, Guid.NewGuid(), "файл.txt");

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task Failed_Storage_Leaves_Nothing_Behind()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        context.Storage.FailOnPut = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(context, task.Id, "файл.txt"));

        Assert.Empty(context.Attachments.All);
        Assert.Empty(context.Storage.Objects);
    }

    [Fact]
    public async Task Reader_Cannot_Attach()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var reader = AddUser(context.Users, UserRole.Reader, "reader");

        await Assert.ThrowsAsync<ForbiddenException>(() => UploadAsync(context, task.Id, "файл.txt", actorId: reader));
    }

    [Fact]
    public async Task Author_And_Admin_Can_Delete_Others_Cannot()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var developer = AddUser(context.Users, UserRole.Developer, "dev");
        var admin = AddUser(context.Users, UserRole.Admin, "admin");

        var mine = (await UploadAsync(context, task.Id, "моё.txt", Encoding.UTF8.GetBytes("a"))).Response!;
        var other = (await UploadAsync(context, task.Id, "чужое.txt", Encoding.UTF8.GetBytes("b"), actorId: developer)).Response!;

        // Developer не автор первого файла и не Admin — удалять чужое ему нельзя.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            context.Mediator.Send(new AttachmentDeleteCommand(developer, mine.Id), CancellationToken.None));

        Assert.True(await context.Mediator.Send(new AttachmentDeleteCommand(developer, other.Id), CancellationToken.None));
        Assert.True(await context.Mediator.Send(new AttachmentDeleteCommand(admin, mine.Id), CancellationToken.None));
        Assert.Empty(context.Attachments.All);
    }

    [Fact]
    public async Task Delete_Removes_Object_And_Writes_Journal()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var uploaded = (await UploadAsync(context, task.Id, "макет.pdf")).Response!;

        Assert.True(await context.Mediator.Send(new AttachmentDeleteCommand(Owner, uploaded.Id), CancellationToken.None));

        Assert.Empty(context.Attachments.All);
        Assert.Empty(context.Storage.Objects);
        var entry = Assert.Single(context.Activities.ForTask(task.Id), a => a.Type == TaskActivityType.AttachmentRemoved);
        Assert.Equal("макет.pdf", entry.NewValue);
    }

    [Fact]
    public async Task Delete_Of_Unknown_Attachment_Is_False()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        Assert.False(await context.Mediator.Send(new AttachmentDeleteCommand(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task List_Returns_Attachments_And_Null_For_Unknown_Task()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        await UploadAsync(context, task.Id, "первый.txt", Encoding.UTF8.GetBytes("1"));
        await UploadAsync(context, task.Id, "второй.txt", Encoding.UTF8.GetBytes("2"));

        var list = await context.Mediator.Send(new AttachmentListQuery(task.Id), CancellationToken.None);

        Assert.Equal(2, list!.Count);
        Assert.Null(await context.Mediator.Send(new AttachmentListQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Content_Comes_Back_Byte_For_Byte()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var uploaded = (await UploadAsync(context, task.Id, "снимок.png", Png)).Response!;

        var content = await context.Mediator.Send(new AttachmentContentQuery(uploaded.Id), CancellationToken.None);

        using var buffer = new MemoryStream();
        await content!.Content.CopyToAsync(buffer);
        Assert.Equal(Png, buffer.ToArray());
        Assert.Equal("снимок.png", content.FileName);
        Assert.True(content.CanInline);
    }

    [Fact]
    public async Task Content_Is_Null_When_Object_Disappeared()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        var uploaded = (await UploadAsync(context, task.Id, "макет.pdf")).Response!;

        // Объект вычистили из бакета руками: строка есть, файла нет — для клиента это 404, а не 500.
        var attachment = context.Attachments.All.Single();
        await context.Storage.DeleteAsync(attachment.StorageKey, CancellationToken.None);

        Assert.Null(await context.Mediator.Send(new AttachmentContentQuery(uploaded.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_Task_Takes_Its_Objects_With_It()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);
        await UploadAsync(context, task.Id, "смета.xlsx", Encoding.UTF8.GetBytes("1"));
        await UploadAsync(context, task.Id, "договор.pdf", Encoding.UTF8.GetBytes("2"));

        await context.Mediator.Send(new TaskDeleteCommand(Owner, task.Id), CancellationToken.None);

        // Строки уносит каскад БД, объекты — хендлер: иначе бакет копил бы файлы удалённых задач.
        Assert.Empty(context.Storage.Objects);
        Assert.Equal([$"attachments/{task.BoardId}/{task.Id}/"], context.Storage.PrefixDeletes);
    }

    [Fact]
    public async Task Deleting_Task_Without_Attachments_Does_Not_Touch_Storage()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var task = await CreateTaskAsync(context);

        await context.Mediator.Send(new TaskDeleteCommand(Owner, task.Id), CancellationToken.None);

        // Задач без файлов большинство: лишний поход в хранилище на каждом удалении не нужен.
        Assert.Empty(context.Storage.PrefixDeletes);
    }

    [Fact]
    public async Task Deleting_Board_Takes_Objects_Of_All_Its_Tasks()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!;
        context.Tasks.RegisterBoardStatuses((await context.Boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        var first = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Первая", null, null), CancellationToken.None))!;
        var second = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Вторая", null, null), CancellationToken.None))!;
        await UploadAsync(context, first.Id, "первый.txt", Encoding.UTF8.GetBytes("1"));
        await UploadAsync(context, second.Id, "второй.txt", Encoding.UTF8.GetBytes("2"));

        await context.Mediator.Send(new BoardDeleteCommand(Owner, board.Id), CancellationToken.None);

        // Один префикс на весь проект: перебирать его задачи ради удаления файлов незачем.
        Assert.Empty(context.Storage.Objects);
        Assert.Equal([$"attachments/{board.Id}/"], context.Storage.PrefixDeletes);
    }
}
