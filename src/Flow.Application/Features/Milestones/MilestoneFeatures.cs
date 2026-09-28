using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Milestones;
using MediatR;

namespace Flow.Application.Features.Milestones;

/// <summary>Вехи проекта с прогрессом: открытые, затем закрытые. null — проект скрыт или его нет.</summary>
public sealed record MilestoneListQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<MilestoneResponse>?>;

/// <summary>Одна веха с прогрессом; null — нет или проект скрыт (404).</summary>
public sealed record MilestoneGetQuery(Guid ActorId, Guid MilestoneId) : IRequest<MilestoneResponse?>;

/// <summary>Новая открытая веха в конце списка проекта; имя уникально в проекте без учёта регистра. Права — ManageMilestones.</summary>
public sealed record MilestoneCreateCommand(Guid ActorId, Guid BoardId, string Name, string? Description = null, DateOnly? TargetDate = null)
    : IRequest<MilestoneResponse?>;

/// <summary>PATCH вехи: null — не трогать; Clear* снимают; Closed закрывает или открывает снова. Права — ManageMilestones.</summary>
public sealed record MilestoneUpdateCommand(
    Guid ActorId,
    Guid MilestoneId,
    string? Name = null,
    string? Description = null,
    bool ClearDescription = false,
    DateOnly? TargetDate = null,
    bool ClearTargetDate = false,
    bool? Closed = null) : IRequest<MilestoneResponse?>;

/// <summary>Удаление вехи: задачи теряют веху с записью MilestoneChanged в журнале. false — вехи нет. Права — ManageMilestones.</summary>
public sealed record MilestoneDeleteCommand(Guid ActorId, Guid MilestoneId) : IRequest<bool>;

/// <summary>
/// Общая веха (этап 2H): весь список проектов, где она доступна. Права — ManageMilestones в проекте-владельце и в каждом
/// добавляемом проекте. Из убранного проекта задачи выходят из вехи с записью в журнале. null — вехи нет.
/// </summary>
public sealed record MilestoneShareCommand(Guid ActorId, Guid MilestoneId, IReadOnlyList<Guid> BoardIds) : IRequest<MilestoneResponse?>;

/// <summary>Поле «Веха» в карточке: веха своего проекта (или общая с ним) или null. Это правка задачи (EnsureCanEditTask); закрытая или чужая — 400.</summary>
public sealed record TaskSetMilestoneCommand(Guid ActorId, Guid TaskId, Guid? MilestoneId) : IRequest<TaskUpdateResult>;

internal sealed class MilestoneListQueryHandler(
    IMilestoneRepository milestones, MilestoneProgressCalculator progress, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<MilestoneListQuery, IReadOnlyList<MilestoneResponse>?>
{
    public async Task<IReadOnlyList<MilestoneResponse>?> Handle(MilestoneListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;

        return await progress.ToResponsesAsync(await milestones.GetByBoardAsync(request.BoardId, cancellationToken), cancellationToken);
    }
}

internal sealed class MilestoneGetQueryHandler(
    IMilestoneRepository milestones, MilestoneProgressCalculator progress, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<MilestoneGetQuery, MilestoneResponse?>
{
    public async Task<MilestoneResponse?> Handle(MilestoneGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var milestone = await milestones.GetByIdAsync(request.MilestoneId, cancellationToken);
        if (milestone is null)
            return null;

        // Общую веху видно из любого её проекта, который видит actor.
        foreach (var boardId in milestone.SharedBoardIds.Prepend(milestone.BoardId))
            if ((await projectAccess.GetAsync(actor, boardId, cancellationToken)).CanView)
                return await progress.ToResponseAsync(milestone, cancellationToken);
        return null;
    }
}

internal sealed class MilestoneCreateCommandHandler(
    IBoardRepository boards,
    IMilestoneRepository milestones,
    MilestoneProgressCalculator progress,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<MilestoneCreateCommand, MilestoneResponse?>
{
    public async Task<MilestoneResponse?> Handle(MilestoneCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageMilestones(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;

        var milestone = Milestone.Create(request.BoardId, request.Name, request.Description, request.TargetDate,
            await milestones.NextSortOrderAsync(request.BoardId, cancellationToken));
        await MilestoneNames.EnsureUniqueAsync(milestones, milestone.BoardId, milestone.Name, null, cancellationToken);
        milestones.Add(milestone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await progress.ToResponseAsync(milestone, cancellationToken);
    }
}

internal sealed class MilestoneUpdateCommandHandler(
    IMilestoneRepository milestones,
    MilestoneProgressCalculator progress,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<MilestoneUpdateCommand, MilestoneResponse?>
{
    public async Task<MilestoneResponse?> Handle(MilestoneUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var milestone = await milestones.GetByIdAsync(request.MilestoneId, cancellationToken);
        if (milestone is null)
            return null;

        permissions.EnsureCanManageMilestones(await projectAccess.GetAsync(actor, milestone.BoardId, cancellationToken));

        if (request.Name is not null)
        {
            await MilestoneNames.EnsureUniqueAsync(milestones, milestone.BoardId, request.Name.Trim(), milestone.Id, cancellationToken);
            milestone.Rename(request.Name);
        }
        if (request.Description is not null || request.ClearDescription)
            milestone.SetDescription(request.ClearDescription ? null : request.Description);
        if (request.TargetDate is not null || request.ClearTargetDate)
            milestone.SetTargetDate(request.ClearTargetDate ? null : request.TargetDate);
        if (request.Closed is true && !milestone.IsClosed)
            milestone.Close(DateTime.UtcNow);
        else if (request.Closed is false && milestone.IsClosed)
            milestone.Reopen();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await progress.ToResponseAsync(milestone, cancellationToken);
    }
}

internal sealed class MilestoneDeleteCommandHandler(
    IMilestoneRepository milestones,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<MilestoneDeleteCommand, bool>
{
    public async Task<bool> Handle(MilestoneDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var milestone = await milestones.GetByIdAsync(request.MilestoneId, cancellationToken);
        if (milestone is null)
            return false;

        permissions.EnsureCanManageMilestones(await projectAccess.GetAsync(actor, milestone.BoardId, cancellationToken));

        // FK SetNull снял бы веху и сам, но мимо журнала: у задачи «веха: X → —» должно остаться.
        foreach (var task in await tasks.GetByMilestoneIdAsync(milestone.Id, cancellationToken))
        {
            task.SetMilestone(null);
            activities.Add(TaskActivity.MilestoneChanged(task.Id, actor.Id, milestone.Id, null));
        }

        milestones.Remove(milestone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class TaskSetMilestoneCommandHandler(
    ITaskItemRepository tasks,
    TaskMilestones taskMilestones,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskSetMilestoneCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetMilestoneCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (await taskMilestones.MoveAsync(task, request.MilestoneId, actor.Id, cancellationToken))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }
}

internal sealed class MilestoneShareCommandHandler(
    IBoardRepository boards,
    IMilestoneRepository milestones,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    MilestoneProgressCalculator progress,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<MilestoneShareCommand, MilestoneResponse?>
{
    public async Task<MilestoneResponse?> Handle(MilestoneShareCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var milestone = await milestones.GetByIdAsync(request.MilestoneId, cancellationToken);
        if (milestone is null)
            return null;

        permissions.EnsureCanManageMilestones(await projectAccess.GetAsync(actor, milestone.BoardId, cancellationToken));

        var wanted = request.BoardIds.Where(id => id != milestone.BoardId).Distinct().ToList();
        foreach (var boardId in wanted.Except(milestone.SharedBoardIds))
        {
            permissions.EnsureCanManageMilestones(await projectAccess.GetAsync(actor, boardId, cancellationToken));
            var board = await boards.GetByIdAsync(boardId, cancellationToken)
                        ?? throw new InvalidOperationException("Проект не найден.");
            // По имени вехи ищет FQL: в одном проекте двух вех с одним именем быть не должно.
            if ((await milestones.GetByBoardAsync(boardId, cancellationToken)).Any(m => m.Id != milestone.Id
                    && string.Equals(m.Name, milestone.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"В проекте {board.Key} уже есть веха «{milestone.Name}».");
        }

        var removed = milestone.SharedBoardIds.Except(wanted).ToHashSet();
        if (removed.Count > 0)
            foreach (var task in (await tasks.GetByMilestoneIdAsync(milestone.Id, cancellationToken)).Where(t => removed.Contains(t.BoardId)))
            {
                task.SetMilestone(null);
                activities.Add(TaskActivity.MilestoneChanged(task.Id, actor.Id, milestone.Id, null));
            }

        milestone.ShareWith(wanted);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await progress.ToResponseAsync(milestone, cancellationToken);
    }
}

/// <summary>Имя вехи уникально в проекте без учёта регистра: по нему FQL находит веху (`milestone = "1.0"`).</summary>
internal static class MilestoneNames
{
    public static async Task EnsureUniqueAsync(IMilestoneRepository milestones, Guid boardId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var taken = (await milestones.GetByBoardAsync(boardId, cancellationToken))
            .Any(m => m.Id != exceptId && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new InvalidOperationException($"Веха «{name}» в проекте уже есть.");
    }
}
