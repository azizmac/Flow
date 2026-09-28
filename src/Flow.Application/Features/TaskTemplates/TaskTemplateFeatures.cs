using System.Text.Json;
using System.Text.Json.Nodes;
using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Tasks;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.TaskTemplates;

// Шаблоны задач (docs/TZ_workflow_config.md §5, этап 3G). Видят все, кто видит проект (форма создания); создавать,
// править и удалять — ManageTaskTemplates (Developer+). Шаблон — не задача: у него нет статуса, исполнителя и истории,
// поэтому ни журнала, ни индекса поиска здесь нет.

public sealed record TaskTemplateListQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<TaskTemplateResponse>?>;

public sealed record TaskTemplateCreateCommand(Guid ActorId, Guid BoardId, SaveTaskTemplateRequest Template) : IRequest<TaskTemplateResponse?>;

/// <summary>Правка — полная замена содержимого. null — шаблона нет.</summary>
public sealed record TaskTemplateUpdateCommand(Guid ActorId, Guid TemplateId, SaveTaskTemplateRequest Template) : IRequest<TaskTemplateResponse?>;

public sealed record TaskTemplateDeleteCommand(Guid ActorId, Guid TemplateId) : IRequest<bool>;

/// <summary>«Сохранить задачу как шаблон»: снимок задачи и её прямых подзадач. null — задачи нет.</summary>
public sealed record TaskTemplateFromTaskCommand(Guid ActorId, Guid TaskId, string Name) : IRequest<TaskTemplateResponse?>;

internal static class TaskTemplateMapping
{
    public static TaskTemplateResponse ToResponse(this TaskTemplate template)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(template.CustomFieldsJson) ?? [];
        return new TaskTemplateResponse(
            template.Id, template.BoardId, template.Name, template.TitlePattern,
            template.RenderTitle(DateOnly.FromDateTime(DateTime.UtcNow)),
            template.TypeId, template.Priority.ToResponsePriority(), template.Description, fields, template.Checklist.ToList(),
            template.Subtasks.Select(s => new TaskTemplateSubtaskDto(s.Title, s.TypeId, s.Checklist.ToList())).ToList(),
            template.CreatedById, template.CreatedAt, template.UpdatedAt, template.UsageCount);
    }

    /// <summary>
    /// Содержимое шаблона с проверкой по проекту: типы — свои и не архивные, подзадача строго ниже типа задачи,
    /// значения полей — через CustomFieldValidator (тот же разбор, что у задачи).
    /// </summary>
    public static void Apply(this TaskTemplate template, Board board, SaveTaskTemplateRequest request)
    {
        TaskType? type = null;
        if (request.TypeId is { } typeId)
        {
            type = board.GetTaskType(typeId);
            if (type.IsArchived)
                throw new InvalidOperationException($"Тип «{type.Name}» в архиве.");
        }

        foreach (var sub in request.Subtasks ?? [])
        {
            if (sub.TypeId is not { } subTypeId)
                continue;
            var subType = board.GetTaskType(subTypeId);
            if (subType.IsArchived)
                throw new InvalidOperationException($"Тип «{subType.Name}» в архиве.");
            if (type is not null && subType.Level <= type.Level)
                throw new InvalidOperationException($"Подзадача «{sub.Title}» должна быть уровнем ниже, чем «{type.Name}».");
        }

        var values = new Dictionary<Guid, JsonNode>();
        foreach (var (fieldId, value) in request.CustomFields ?? new Dictionary<Guid, JsonElement?>())
        {
            var field = board.CustomFields.FirstOrDefault(f => f.Id == fieldId && !f.IsArchived)
                        ?? throw new InvalidOperationException($"Поля {fieldId} в проекте нет.");
            if (value is { } element && CustomFieldValidator.Validate(field, element) is { } node)
                values[fieldId] = node;
        }

        template.Rename(request.Name);
        template.SetTitlePattern(request.TitlePattern);
        template.SetDefaults(request.TypeId, request.Priority.ToDomainPriority(), request.Description);
        template.SetCustomFields(values);
        template.SetChecklist(request.Checklist ?? []);
        template.SetSubtasks((request.Subtasks ?? []).Select(s => new TaskTemplateSubtask(s.Title, s.TypeId, s.Checklist ?? [])));
    }
}

internal sealed class TaskTemplateHandlers(
    ITaskTemplateRepository templates,
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<TaskTemplateListQuery, IReadOnlyList<TaskTemplateResponse>?>,
    IRequestHandler<TaskTemplateCreateCommand, TaskTemplateResponse?>,
    IRequestHandler<TaskTemplateUpdateCommand, TaskTemplateResponse?>,
    IRequestHandler<TaskTemplateDeleteCommand, bool>,
    IRequestHandler<TaskTemplateFromTaskCommand, TaskTemplateResponse?>
{
    public async Task<IReadOnlyList<TaskTemplateResponse>?> Handle(TaskTemplateListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;
        return (await templates.GetByBoardAsync(request.BoardId, cancellationToken)).Select(t => t.ToResponse()).ToList();
    }

    public async Task<TaskTemplateResponse?> Handle(TaskTemplateCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageTaskTemplates(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var template = await NewAsync(board, request.Template.Name, request.Template.TitlePattern, actor.Id, cancellationToken);
        template.Apply(board, request.Template);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template.ToResponse();
    }

    public async Task<TaskTemplateResponse?> Handle(TaskTemplateUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var template = await templates.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
            return null;
        permissions.EnsureCanManageTaskTemplates(await projectAccess.GetAsync(actor, template.BoardId, cancellationToken));
        var board = await boards.GetByIdAsync(template.BoardId, cancellationToken) ?? throw new ProjectNotFoundException();

        await EnsureNameFreeAsync(board.Id, request.Template.Name, template.Id, cancellationToken);
        template.Apply(board, request.Template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template.ToResponse();
    }

    public async Task<bool> Handle(TaskTemplateDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var template = await templates.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
            return false;
        permissions.EnsureCanManageTaskTemplates(await projectAccess.GetAsync(actor, template.BoardId, cancellationToken));

        templates.Remove(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskTemplateResponse?> Handle(TaskTemplateFromTaskCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;
        permissions.EnsureCanManageTaskTemplates(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken));
        var board = await boards.GetByIdAsync(task.BoardId, cancellationToken) ?? throw new ProjectNotFoundException();

        var children = (await tasks.GetChildrenAsync(task.Id, cancellationToken)).OrderBy(c => c.Rank, StringComparer.Ordinal).Take(TaskTemplate.MaxSubtasks);
        var liveFields = board.CustomFields.Where(f => !f.IsArchived).Select(f => f.Id).ToHashSet();
        var values = (JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(task.CustomFieldsJson) ?? [])
            .Where(p => liveFields.Contains(p.Key))
            .ToDictionary(p => p.Key, p => (JsonElement?)p.Value);
        var type = board.GetTaskType(task.TypeId);

        var snapshot = new SaveTaskTemplateRequest(
            request.Name,
            task.Title,
            type.IsArchived ? null : type.Id,
            task.Priority.ToResponsePriority(),
            task.Description,
            values,
            task.Checklist.Select(i => i.Text).ToList(),
            children.Select(c => new TaskTemplateSubtaskDto(c.Title,
                board.TaskTypes.FirstOrDefault(t => t.Id == c.TypeId) is { IsArchived: false } childType ? childType.Id : null,
                c.Checklist.Select(i => i.Text).ToList())).ToList());

        var template = await NewAsync(board, request.Name, task.Title, actor.Id, cancellationToken);
        template.Apply(board, snapshot);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template.ToResponse();
    }

    private async Task<TaskTemplate> NewAsync(Board board, string name, string titlePattern, Guid actorId, CancellationToken cancellationToken)
    {
        await EnsureNameFreeAsync(board.Id, name, null, cancellationToken);
        var existing = await templates.GetByBoardAsync(board.Id, cancellationToken);
        return TaskTemplate.Create(board.Id, name, titlePattern, actorId, existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder) + 1);
    }

    /// <summary>Имя уникально в проекте без учёта регистра: в меню «Из шаблона…» два одинаковых не различить.</summary>
    private async Task EnsureNameFreeAsync(Guid boardId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? "";
        if ((await templates.GetByBoardAsync(boardId, cancellationToken))
            .Any(t => t.Id != exceptId && string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Шаблон «{trimmed}» в проекте уже есть.");
    }
}
