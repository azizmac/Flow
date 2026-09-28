using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using WorkflowMode = Flow.Shared.Contracts.Boards.WorkflowMode;

namespace Flow.Application.Features.Boards.Workflow;

// Workflow проекта (docs/TZ_workflow_config.md §2). Читать — любой, кто видит проект; менять — право настройки
// проекта (ManageConfig). Заменяется целиком. Тупики в Restricted — 400 со списком статусов.

public sealed record WorkflowGetQuery(Guid ActorId, Guid BoardId) : IRequest<WorkflowResponse?>;

/// <summary>Layout — раскладка графа (этап 3D): null — не менять, пустой — автораскладка.</summary>
public sealed record WorkflowSetCommand(Guid ActorId, Guid BoardId, WorkflowMode Mode, IReadOnlyList<TransitionRequest> Transitions,
    IReadOnlyList<StatusPosition>? Layout = null) : IRequest<WorkflowResponse?>;

/// <summary>Куда можно перевести задачу; null — задачи нет или она скрыта (404).</summary>
public sealed record TaskTransitionsQuery(Guid ActorId, Guid TaskId) : IRequest<IReadOnlyList<TaskTransitionResponse>?>;

internal sealed class WorkflowHandlers(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    TransitionGuard guard,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<WorkflowGetQuery, WorkflowResponse?>,
    IRequestHandler<WorkflowSetCommand, WorkflowResponse?>,
    IRequestHandler<TaskTransitionsQuery, IReadOnlyList<TaskTransitionResponse>?>
{
    public async Task<WorkflowResponse?> Handle(WorkflowGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;

        return (await boards.GetByIdAsync(request.BoardId, cancellationToken))?.ToWorkflowResponse();
    }

    public async Task<WorkflowResponse?> Handle(WorkflowSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        if (!Enum.IsDefined(request.Mode))
            throw new ArgumentException($"Unknown workflow mode {request.Mode}.", nameof(request.Mode));

        board.SetWorkflow((Domain.Entities.WorkflowMode)(int)request.Mode, request.Transitions.Select(t => t.ToSpec()).ToList());
        if (request.Layout is { } layout)
            board.SetStatusLayout(layout.Select(p => (p.StatusId, p.X, p.Y)).ToList());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return board.ToWorkflowResponse();
    }

    public async Task<IReadOnlyList<TaskTransitionResponse>?> Handle(TaskTransitionsQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;

        var access = await projectAccess.GetAsync(actor, task.BoardId, cancellationToken);
        if (!access.CanView)
            return null;

        var board = await boards.GetByIdAsync(task.BoardId, cancellationToken);
        if (board is null)
            return null;

        var context = await guard.ContextAsync(access, board, task, cancellationToken);
        return board.Statuses
            .Where(s => s.Id != task.StatusId)
            .OrderBy(s => s.SortOrder)
            .Select(s =>
            {
                var check = board.CheckTransition(task.StatusId, s.Id, context);
                return new TaskTransitionResponse(s.Id, check.Allowed, check.Reasons);
            })
            .ToList();
    }
}
