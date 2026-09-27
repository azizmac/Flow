using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using DomainFrequency = Flow.Domain.Entities.RecurrenceFrequency;
using SharedFrequency = Flow.Shared.Contracts.Tasks.RecurrenceFrequency;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Application.Features.Tasks.Recurrence;

// Повторяющиеся задачи (docs/TZ_task_model.md §9, этап 1F): правило на задаче-образце и генерация копий.

/// <summary>Правило задачи; null — задачи нет, она скрыта или правила нет.</summary>
public sealed record TaskRecurrenceGetQuery(Guid ActorId, Guid TaskId) : IRequest<TaskRecurrenceResponse?>;

/// <summary>Поставить или поменять правило (права — правка образца). null — задачи нет.</summary>
public sealed record TaskRecurrenceSetCommand(Guid ActorId, Guid TaskId, TaskRecurrenceRequest Rule) : IRequest<TaskRecurrenceResponse?>;

/// <summary>Снять правило; уже созданные копии остаются. false — правила нет.</summary>
public sealed record TaskRecurrenceDeleteCommand(Guid ActorId, Guid TaskId) : IRequest<bool>;

/// <summary>Ближайшие даты: Rule — правило, которое ещё только редактируется; null — сохранённое. null в ответе — задачи или правила нет.</summary>
public sealed record TaskRecurrencePreviewQuery(Guid ActorId, Guid TaskId, TaskRecurrenceRequest? Rule, int Count = 5) : IRequest<IReadOnlyList<DateOnly>?>;

/// <summary>Id активных правил — генератор обходит их по одному.</summary>
public sealed record RecurrenceActiveIdsQuery : IRequest<IReadOnlyList<Guid>>;

/// <summary>Создать копии одного правила на день Today; ответ — сколько создано.</summary>
public sealed record RecurrenceGenerateCommand(Guid RecurrenceId, DateOnly Today) : IRequest<int>;

/// <summary>Генерация правила сорвалась — причина видна в интерфейсе, правило остаётся включённым и повторит на следующем проходе.</summary>
public sealed record RecurrenceRecordErrorCommand(Guid RecurrenceId, string Error) : IRequest;

internal sealed class RecurrenceRecordErrorCommandHandler(ITaskRecurrenceRepository recurrences, IUnitOfWork unitOfWork)
    : IRequestHandler<RecurrenceRecordErrorCommand>
{
    public async Task Handle(RecurrenceRecordErrorCommand request, CancellationToken cancellationToken)
    {
        if (await recurrences.GetByIdAsync(request.RecurrenceId, cancellationToken) is not { } recurrence)
            return;
        recurrence.RecordError(request.Error);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class RecurrenceMapping
{
    public const int MaxPreview = 20;

    public static RecurrenceRule ToRule(this TaskRecurrenceRequest request) =>
        RecurrenceRule.Create((DomainFrequency)(int)request.Frequency, request.Interval, request.WeekDays, request.MonthDay);

    public static TaskRecurrenceResponse ToResponse(this TaskRecurrence r, DateOnly today) => new(
        r.TemplateTaskId, (SharedFrequency)(int)r.Rule.Frequency, r.Rule.Interval, r.Rule.Days, r.Rule.MonthDay, r.StartsOn, r.EndsOn,
        r.LeadDays, r.DueOffsetDays, r.CopyAssignee, r.CopyChecklist, r.IsActive, r.CreatedById, r.GeneratedUntil, r.LastError,
        r.NextDates(today, 5));
}

internal sealed class TaskRecurrenceHandlers(
    ITaskItemRepository tasks,
    ITaskRecurrenceRepository recurrences,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    RecurrenceOptions options,
    IUnitOfWork unitOfWork) :
    IRequestHandler<TaskRecurrenceGetQuery, TaskRecurrenceResponse?>,
    IRequestHandler<TaskRecurrenceSetCommand, TaskRecurrenceResponse?>,
    IRequestHandler<TaskRecurrenceDeleteCommand, bool>,
    IRequestHandler<TaskRecurrencePreviewQuery, IReadOnlyList<DateOnly>?>,
    IRequestHandler<RecurrenceActiveIdsQuery, IReadOnlyList<Guid>>
{
    private DateOnly Today => options.Today(DateTime.UtcNow);

    public async Task<TaskRecurrenceResponse?> Handle(TaskRecurrenceGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        return (await recurrences.GetByTemplateAsync(task.Id, cancellationToken))?.ToResponse(Today);
    }

    public async Task<TaskRecurrenceResponse?> Handle(TaskRecurrenceSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        var r = request.Rule;
        var rule = r.ToRule();
        var recurrence = await recurrences.GetByTemplateAsync(task.Id, cancellationToken);
        if (recurrence is null)
        {
            recurrence = TaskRecurrence.Create(task, actor.Id, rule, r.StartsOn, r.EndsOn, r.LeadDays, r.DueOffsetDays, r.CopyAssignee, r.CopyChecklist);
            recurrences.Add(recurrence);
        }
        else
        {
            recurrence.Update(rule, r.StartsOn, r.EndsOn, r.LeadDays, r.DueOffsetDays, r.CopyAssignee, r.CopyChecklist);
        }

        // Включение — от имени того, кто включил: пауза из-за деактивированного автора снимается вместе с ним.
        if (r.IsActive && (!recurrence.IsActive || recurrence.LastError is not null))
            recurrence.Resume(actor.Id);
        else if (!r.IsActive && recurrence.IsActive)
            recurrence.Pause();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return recurrence.ToResponse(Today);
    }

    public async Task<bool> Handle(TaskRecurrenceDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);
        var recurrence = await recurrences.GetByTemplateAsync(task.Id, cancellationToken);
        if (recurrence is null)
            return false;

        recurrences.Remove(recurrence);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DateOnly>?> Handle(TaskRecurrencePreviewQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var count = Math.Clamp(request.Count, 1, RecurrenceMapping.MaxPreview);
        if (request.Rule is not { } r)
            return (await recurrences.GetByTemplateAsync(task.Id, cancellationToken))?.NextDates(Today, count);

        var draft = TaskRecurrence.Create(task, actor.Id, r.ToRule(), r.StartsOn, r.EndsOn, r.LeadDays, r.DueOffsetDays, r.CopyAssignee, r.CopyChecklist);
        return draft.NextDates(Today, count);
    }

    public Task<IReadOnlyList<Guid>> Handle(RecurrenceActiveIdsQuery request, CancellationToken cancellationToken) =>
        recurrences.GetActiveIdsAsync(cancellationToken);
}

/// <summary>
/// Генерация копий одного правила (§9). Копия — обычная задача: название, описание, тип (архивный — по умолчанию),
/// приоритет, поля, родитель, чек-лист (снятый) и исполнитель, если так задано и он активен; статус — начальный,
/// срок — дата + DueOffsetDays, автор — автор правила, связь Clones → образец, журнал «создана по расписанию».
/// Образца нет или автор деактивирован — правило на паузе с причиной. Вхождение (правило, дата) — ключ: повтор и
/// второй хост не создадут дубль, а удалённая копия не вернётся.
/// </summary>
internal sealed class RecurrenceGenerateCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    IUserRepository users,
    ITaskLinkRepository links,
    ITaskRecurrenceRepository recurrences,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    IUnitOfWork unitOfWork) : IRequestHandler<RecurrenceGenerateCommand, int>
{
    public async Task<int> Handle(RecurrenceGenerateCommand request, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetByIdAsync(request.RecurrenceId, cancellationToken);
        if (recurrence is null || !recurrence.IsActive)
            return 0;

        var dates = recurrence.DueDates(request.Today);
        if (dates.Count == 0)
            return 0;

        var template = await tasks.GetByIdAsync(recurrence.TemplateTaskId, cancellationToken);
        var author = await users.GetByIdAsync(recurrence.CreatedById, cancellationToken);
        if (template is null || author is not { IsActive: true })
        {
            recurrence.Pause(template is null ? "Образец задачи не найден." : "Автор правила деактивирован — включите повторение заново.");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return 0;
        }

        var board = (await boards.GetByIdAsync(template.BoardId, cancellationToken))!;
        var type = board.GetTaskType(template.TypeId);
        var parent = template.ParentId is { } parentId ? await tasks.GetByIdAsync(parentId, cancellationToken) : null;
        var assignee = recurrence.CopyAssignee && template.AssigneeId is { } assigneeId ? await users.GetByIdAsync(assigneeId, cancellationToken) : null;

        var created = new List<TaskItem>();
        foreach (var date in dates)
        {
            if (await recurrences.OccurrenceExistsAsync(recurrence.Id, date, cancellationToken))
                continue;

            var copy = board.CreateTask(template.Title, template.Description, createdById: author.Id,
                typeId: type.IsArchived ? null : type.Id, rank: FractionalIndex.First);
            if (parent is not null && parent.BoardId == board.Id && board.GetTaskType(parent.TypeId).Level < board.GetTaskType(copy.TypeId).Level)
                copy.SetParent(parent, board.GetTaskType(copy.TypeId), board.GetTaskType(parent.TypeId));
            copy.SetPriority(template.Priority);
            if (copy.TypeId == template.TypeId)
                copy.CopyCustomFieldsFrom(template);
            if (recurrence.CopyChecklist)
                foreach (var item in template.Checklist)
                    copy.AddChecklistItem(item.Text);
            if (assignee is { IsActive: true })
                copy.Assign(assignee.Id);
            if (recurrence.DueOffsetDays is { } offset)
                copy.SetDueDate(date.AddDays(offset));

            tasks.Add(copy);
            recurrences.AddOccurrence(TaskRecurrenceOccurrence.Create(recurrence.Id, date, copy.Id));
            links.Add(TaskLink.Create(copy.Id, template.Id, TaskLinkType.Clones, author.Id));
            activities.Add(TaskActivity.CreatedByRecurrence(copy.Id, author.Id));
            if (parent is not null && copy.ParentId == parent.Id)
                activities.Add(TaskActivity.ChildAdded(parent.Id, author.Id, copy.Id));
            searchIndex.Enqueue(SearchSourceType.Task, copy.Id, board.Id, SearchIndexOperation.Upsert);
            created.Add(copy);
        }

        recurrence.MarkGenerated(dates[^1]);

        async Task AssignRanks()
        {
            var previous = await tasks.GetMaxRankAsync(board.Id, null, cancellationToken);
            foreach (var copy in created)
            {
                copy.SetRank(FractionalIndex.Between(previous, null));
                previous = copy.Rank;
            }
        }

        await AssignRanks();
        await TaskRanks.SaveAsync(unitOfWork, AssignRanks, cancellationToken);
        return created.Count;
    }
}
