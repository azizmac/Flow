using System.Text;
using System.Text.Json;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Attachments.Queries.AttachmentListQuery;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;
using CustomFieldType = Flow.Domain.Entities.CustomFieldType;
using SharedActivity = Flow.Shared.Contracts.Tasks.TaskActivityType;
using SharedLinkType = Flow.Shared.Contracts.Tasks.TaskLinkType;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Слияние, разделение, перенос (docs/TZ_task_model.md §6) на фейках: комментарии и вложения переходят, повторы по
/// хешу остаются, дети и уровни, связь «дубль», части разделения, карты статусов и типов, потеря полей, алиас кода,
/// копия объектов на InMemoryFileStorage. SQL алиасов и каскадов — Flow.Infrastructure.Tests.
/// </summary>
public class TaskRestructureFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> BoardAsync(IMediator mediator, string key) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект " + key, key), CancellationToken.None)).Response!;

    private static async Task<TaskResponse> TaskAsync(IMediator mediator, BoardResponse board, string title, Guid? typeId = null, Guid? parentId = null,
        IReadOnlyDictionary<Guid, JsonElement?>? fields = null) =>
        (await mediator.Send(new TaskCreateCommand(Owner, board.Id, title, null, null, typeId, ParentId: parentId, CustomFields: fields), CancellationToken.None))!;

    private static Guid TypeOf(BoardResponse board, SharedTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind).Id;

    private static Task Upload(AttachmentTestContext context, Guid taskId, string name, string content) =>
        context.Mediator.Send(new AttachmentUploadCommand(Owner, taskId, name, Encoding.UTF8.GetByteCount(content), new MemoryStream(Encoding.UTF8.GetBytes(content))), CancellationToken.None);

    private static async Task<IReadOnlyList<TaskActivityResponse>> Journal(IMediator mediator, Guid taskId) =>
        (await mediator.Send(new TaskActivityListQuery(Owner, taskId), CancellationToken.None))!;

    [Fact]
    public async Task Merge_Moves_Comments_Files_Links_And_Children_And_Closes_The_Duplicate()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var mediator = context.Mediator;
        var board = await BoardAsync(mediator, "MRG");
        var source = await TaskAsync(mediator, board, "Дубль", TypeOf(board, SharedTypeKind.Story));
        var target = await TaskAsync(mediator, board, "Основная", TypeOf(board, SharedTypeKind.Story));
        var child = await TaskAsync(mediator, board, "Подзадача", TypeOf(board, SharedTypeKind.Task), parentId: source.Id);
        var other = await TaskAsync(mediator, board, "Соседняя");

        await mediator.Send(new TaskCommentAddCommand(Owner, source.Id, "Шаги воспроизведения"), CancellationToken.None);
        await Upload(context, source.Id, "лог.txt", "лог ошибки");
        await Upload(context, source.Id, "копия.txt", "одинаковое");
        await Upload(context, target.Id, "оригинал.txt", "одинаковое");
        await mediator.Send(new TaskLinkCreateCommand(Owner, source.Id, TaskLinkType.Blocks, other.Id), CancellationToken.None);
        await mediator.Send(new TaskLinkCreateCommand(Owner, source.Id, TaskLinkType.RelatesTo, target.Id), CancellationToken.None);

        var merged = await mediator.Send(new TaskMergeCommand(Owner, source.Id, target.Id), CancellationToken.None);
        Assert.Equal(target.Id, merged!.Id);

        var comment = Assert.Single((await mediator.Send(new TaskCommentListQuery(Owner, target.Id), CancellationToken.None))!);
        Assert.StartsWith($"_из {source.Code}_", comment.Body);

        // Повтор по хешу остался у дубля, второй файл переехал вместе с объектом под ключ основной задачи.
        var targetFiles = (await mediator.Send(new AttachmentListQuery(Owner, target.Id), CancellationToken.None))!;
        Assert.Equal(["лог.txt", "оригинал.txt"], targetFiles.Select(f => f.FileName).Order());
        Assert.Equal(["копия.txt"], (await mediator.Send(new AttachmentListQuery(Owner, source.Id), CancellationToken.None))!.Select(f => f.FileName));
        Assert.Contains(context.Storage.Objects.Keys, k => k.StartsWith(Attachment.TaskPrefix(board.Id, target.Id)));
        Assert.Equal(2, context.Storage.Objects.Keys.Count(k => k.StartsWith(Attachment.TaskPrefix(board.Id, target.Id))));

        var targetLinks = (await mediator.Send(new TaskLinkListQuery(Owner, target.Id), CancellationToken.None))!;
        Assert.Contains(targetLinks, l => l.Type == SharedLinkType.Blocks && l.Outward && l.Other.Id == other.Id);
        Assert.Contains(targetLinks, l => l.Type == SharedLinkType.Duplicates && !l.Outward && l.Other.Id == source.Id);

        Assert.Equal(target.Id, (await mediator.Send(new TaskGetQuery(Owner, child.Id), CancellationToken.None))!.ParentId);
        var closed = (await mediator.Send(new TaskGetQuery(Owner, source.Id), CancellationToken.None))!;
        Assert.Equal(board.Statuses.Single(s => s.IsFinal).Id, closed.StatusId);
        Assert.Contains(await Journal(mediator, source.Id), a => a.Type == SharedActivity.Merged && a.NewValue == target.Id.ToString());
        Assert.Contains(await Journal(mediator, target.Id), a => a.Type == SharedActivity.Merged && a.OldValue == source.Id.ToString());
    }

    [Fact]
    public async Task Merge_Refuses_Self_Ancestor_And_Children_That_Would_Not_Fit()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator, "MRF");
        var epic = await TaskAsync(mediator, board, "Эпик", TypeOf(board, SharedTypeKind.Epic));
        var story = await TaskAsync(mediator, board, "История", TypeOf(board, SharedTypeKind.Story), parentId: epic.Id);
        var subtask = await TaskAsync(mediator, board, "Подзадача", TypeOf(board, SharedTypeKind.Subtask));
        var withChild = await TaskAsync(mediator, board, "Задача", TypeOf(board, SharedTypeKind.Task));
        await TaskAsync(mediator, board, "Ребёнок", TypeOf(board, SharedTypeKind.Subtask), parentId: withChild.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskMergeCommand(Owner, story.Id, story.Id), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskMergeCommand(Owner, story.Id, epic.Id), CancellationToken.None));
        // Подзадача не может стать родителем для подзадачи — отказ до любых изменений.
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskMergeCommand(Owner, withChild.Id, subtask.Id), CancellationToken.None));
        Assert.NotEqual(board.Statuses.Single(s => s.IsFinal).Id, (await mediator.Send(new TaskGetQuery(Owner, withChild.Id), CancellationToken.None))!.StatusId);
    }

    [Fact]
    public async Task Split_Creates_Parts_With_Inherited_Fields_And_Links()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator, "SPL");
        var epic = await TaskAsync(mediator, board, "Эпик", TypeOf(board, SharedTypeKind.Epic));
        var source = await TaskAsync(mediator, board, "Большая", TypeOf(board, SharedTypeKind.Story), parentId: epic.Id);
        await mediator.Send(new TaskAssignCommand(Owner, source.Id, Owner), CancellationToken.None);

        var parts = (await mediator.Send(new TaskSplitCommand(Owner, source.Id, [new SplitPart("Часть 1"), new SplitPart("Часть 2", "детали")]), CancellationToken.None))!;

        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal((epic.Id, source.TypeId, (Guid?)Owner), (p.ParentId!.Value, p.TypeId, p.AssigneeId)));
        var links = (await mediator.Send(new TaskLinkListQuery(Owner, source.Id), CancellationToken.None))!;
        Assert.Equal(2, links.Count(l => l.Type == SharedLinkType.SplitFrom && !l.Outward));
        var split = Assert.Single(await Journal(mediator, source.Id), a => a.Type == SharedActivity.Split);
        Assert.Equal(string.Join(", ", parts.Select(p => p.Code)), split.NewValue);
        Assert.NotEqual(board.Statuses.Single(s => s.IsFinal).Id, (await mediator.Send(new TaskGetQuery(Owner, source.Id), CancellationToken.None))!.StatusId);

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskSplitCommand(Owner, source.Id, []), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new TaskSplitCommand(Owner, source.Id, Enumerable.Range(0, 21).Select(i => new SplitPart($"Часть {i}")).ToList()), CancellationToken.None));
    }

    [Fact]
    public async Task Move_Takes_The_Subtree_Maps_Statuses_Types_And_Fields_And_Keeps_The_Old_Code()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var mediator = context.Mediator;
        var source = await BoardAsync(mediator, "SRC");
        var target = await BoardAsync(mediator, "DST");

        var sla = (await mediator.Send(new CustomFieldCreateCommand(Owner, source.Id, "sla", "SLA", CustomFieldType.Select, ["Gold", "Silver"]), CancellationToken.None))!.CustomFields!.Single();
        var note = (await mediator.Send(new CustomFieldCreateCommand(Owner, source.Id, "note", "Заметка", CustomFieldType.Text), CancellationToken.None))!.CustomFields!.Single(f => f.Key == "note");
        var targetSla = (await mediator.Send(new CustomFieldCreateCommand(Owner, target.Id, "sla", "SLA", CustomFieldType.Select, ["silver", "Bronze"]), CancellationToken.None))!.CustomFields!.Single();

        var root = await TaskAsync(mediator, source, "Корень", TypeOf(source, SharedTypeKind.Story), fields: new Dictionary<Guid, JsonElement?>
        {
            [sla.Id] = JsonDocument.Parse($"\"{sla.Options[1].Id}\"").RootElement.Clone(),
            [note.Id] = JsonDocument.Parse("\"текст\"").RootElement.Clone()
        });
        var child = await TaskAsync(mediator, source, "Ребёнок", TypeOf(source, SharedTypeKind.Task), parentId: root.Id);
        await Upload(context, child.Id, "схема.txt", "схема");

        var preview = (await mediator.Send(new TaskMovePreviewQuery(Owner, root.Id, target.Id), CancellationToken.None))!;
        Assert.Equal((2, 1, false), (preview.TaskCount, preview.AttachmentCount, preview.ParentDropped));
        Assert.Equal([new LostFieldValue(root.Code, "Заметка")], preview.LostFields);
        Assert.Empty(preview.Problems);

        var inWork = target.Statuses.Single(s => s.Name == "В работе").Id;
        var moved = (await mediator.Send(new TaskMoveCommand(Owner, root.Id, target.Id,
            new Dictionary<Guid, Guid> { [root.StatusId] = inWork }), CancellationToken.None))!;

        Assert.Equal((target.Id, "DST-1", inWork), (moved.BoardId, moved.Code, moved.StatusId));
        Assert.Equal(TypeOf(target, SharedTypeKind.Story), moved.TypeId);
        // Вариант «Silver» нашёлся по подписи без учёта регистра, текстовое поле потерялось.
        Assert.Equal($"\"{targetSla.Options[0].Id}\"", Assert.Single(moved.CustomFields!).Value.GetRawText());

        var movedChild = (await mediator.Send(new TaskGetQuery(Owner, child.Id), CancellationToken.None))!;
        Assert.Equal((target.Id, "DST-2", (Guid?)root.Id), (movedChild.BoardId, movedChild.Code, movedChild.ParentId));
        // Карта статусов — по статусу, а не по задаче: у ребёнка тот же исходный статус.
        Assert.Equal(inWork, movedChild.StatusId);

        // Файл скопирован под префикс нового проекта, старый объект удалён.
        Assert.Single(context.Storage.Objects.Keys, k => k.StartsWith(Attachment.TaskPrefix(target.Id, child.Id)));
        Assert.DoesNotContain(context.Storage.Objects.Keys, k => k.StartsWith(Attachment.BoardPrefix(source.Id)));

        // Старый код ведёт на задачу.
        Assert.Equal(root.Id, (await mediator.Send(new TaskGetByCodeQuery(Owner, root.Code.ToLowerInvariant()), CancellationToken.None))!.Id);
        Assert.Contains(await Journal(mediator, root.Id), a => a.Type == SharedActivity.Moved && a.OldValue == root.Code && a.NewValue == "DST-1");
    }

    [Fact]
    public async Task Move_Refuses_When_A_Subtask_Would_Not_Be_Below_Its_Parent()
    {
        var (mediator, boards, _, _) = TestMediatorFactory.Create();
        var source = await BoardAsync(mediator, "SRA");
        var target = await BoardAsync(mediator, "DSA");
        var story = await TaskAsync(mediator, source, "История", TypeOf(source, SharedTypeKind.Story));
        await TaskAsync(mediator, source, "Задача", TypeOf(source, SharedTypeKind.Task), parentId: story.Id);

        // В целевом проекте тип задачи явно отображён на историю — ребёнок стал бы вровень с родителем.
        var typeMap = new Dictionary<Guid, Guid> { [TypeOf(source, SharedTypeKind.Task)] = TypeOf(target, SharedTypeKind.Story) };
        var preview = (await mediator.Send(new TaskMovePreviewQuery(Owner, story.Id, target.Id, TypeMap: typeMap), CancellationToken.None))!;
        Assert.Single(preview.Problems);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskMoveCommand(Owner, story.Id, target.Id, TypeMap: typeMap), CancellationToken.None));
        Assert.Equal(source.Id, (await mediator.Send(new TaskGetQuery(Owner, story.Id), CancellationToken.None))!.BoardId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskMoveCommand(Owner, story.Id, source.Id), CancellationToken.None));
    }
}
