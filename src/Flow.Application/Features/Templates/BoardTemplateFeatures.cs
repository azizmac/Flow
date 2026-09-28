using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Flow.Shared.Contracts.Boards;
using MediatR;
using SharedWorkflowMode = Flow.Shared.Contracts.Boards.WorkflowMode;

namespace Flow.Application.Features.Templates;

// Шаблоны проектов (docs/TZ_workflow_config.md §4, этап 3F). Встроенные — в коде (BuiltInBoardTemplates), сохранённые —
// таблица BoardTemplates с чертежом в JSON. Видят все (форма создания проекта), сохранять и удалять — глобальный Admin+:
// тот же, кто создаёт проекты. Встроенный не удаляется.

public sealed record BoardTemplateListQuery(Guid ActorId) : IRequest<IReadOnlyList<BoardTemplateResponse>>;

/// <summary>Снимок конфигурации проекта; IncludeTasks — до 50 задач верхнего уровня как образец. null — проекта нет.</summary>
public sealed record BoardTemplateSaveCommand(Guid ActorId, Guid BoardId, string Name, string? Description, bool IncludeTasks)
    : IRequest<BoardTemplateResponse?>;

public sealed record BoardTemplateDeleteCommand(Guid ActorId, Guid TemplateId) : IRequest<bool>;

/// <summary>Чертёж шаблона по Id — встроенного или сохранённого (с апгрейдом старой версии); null — такого нет.</summary>
internal static class BoardTemplates
{
    public static async Task<BoardBlueprint?> ResolveAsync(IBoardTemplateRepository templates, Guid id, CancellationToken cancellationToken)
    {
        if (BuiltInBoardTemplates.Find(id) is { } builtIn)
            return builtIn.Blueprint;
        return await templates.GetByIdAsync(id, cancellationToken) is { } saved ? BoardBlueprintJson.Deserialize(saved.Payload, saved.Version) : null;
    }

    public static BoardTemplateResponse ToResponse(this BuiltInBoardTemplate template) =>
        Describe(template.Id, template.Name, template.Description, true, null, null, template.Blueprint);

    public static BoardTemplateResponse ToResponse(this BoardTemplate template) =>
        Describe(template.Id, template.Name, template.Description, false, template.CreatedById, template.CreatedAt,
            BoardBlueprintJson.Deserialize(template.Payload, template.Version));

    private static BoardTemplateResponse Describe(Guid id, string name, string? description, bool builtIn, Guid? createdById, DateTime? createdAt,
        BoardBlueprint blueprint) => new(
        id, name, description, builtIn, createdById, createdAt,
        (SharedWorkflowMode)(int)blueprint.WorkflowMode,
        blueprint.Statuses.Select(s => s.Name).ToList(),
        (blueprint.TaskTypes.Count > 0 ? blueprint.TaskTypes.Select(t => t.Name) : DefaultTaskTypes.All.Select(t => t.Name)).ToList(),
        blueprint.CustomFields.Select(f => f.Name).ToList(),
        blueprint.SampleTasks.Count);
}

internal sealed class BoardTemplateHandlers(
    IBoardTemplateRepository templates,
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<BoardTemplateListQuery, IReadOnlyList<BoardTemplateResponse>>,
    IRequestHandler<BoardTemplateSaveCommand, BoardTemplateResponse?>,
    IRequestHandler<BoardTemplateDeleteCommand, bool>
{
    public async Task<IReadOnlyList<BoardTemplateResponse>> Handle(BoardTemplateListQuery request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);
        var saved = new List<BoardTemplateResponse>();
        foreach (var template in await templates.GetAllAsync(cancellationToken))
        {
            // Битый или слишком новый шаблон не должен ронять весь список формы создания проекта.
            try
            {
                saved.Add(template.ToResponse());
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
            }
        }

        return BuiltInBoardTemplates.All.Select(t => t.ToResponse()).Concat(saved).ToList();
    }

    public async Task<BoardTemplateResponse?> Handle(BoardTemplateSaveCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanCreateBoard(actor);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var blueprint = board.ToBlueprint();
        if (request.IncludeTasks)
        {
            var typeKeys = blueprint.TaskTypes.Select(t => t.Key).ToHashSet();
            var samples = (await tasks.GetByBoardIdAsync(board.Id, null, cancellationToken))
                .Where(t => t.ParentId is null)
                .OrderBy(t => t.Rank, StringComparer.Ordinal)
                .Take(BoardBlueprint.MaxSampleTasks)
                .Select(t => new BlueprintSampleTask(t.Title, t.Description, typeKeys.Contains(t.TypeId.ToString("N")) ? t.TypeId.ToString("N") : null))
                .ToList();
            blueprint = blueprint with { SampleTasks = samples };
        }

        var template = BoardTemplate.Create(request.Name, request.Description, actor.Id, BoardBlueprint.CurrentVersion, BoardBlueprintJson.Serialize(blueprint));
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return template.ToResponse();
    }

    public async Task<bool> Handle(BoardTemplateDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanCreateBoard(actor);
        if (BuiltInBoardTemplates.Find(request.TemplateId) is not null)
            throw new InvalidOperationException("Встроенный шаблон не удаляется.");

        var template = await templates.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
            return false;

        templates.Remove(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
