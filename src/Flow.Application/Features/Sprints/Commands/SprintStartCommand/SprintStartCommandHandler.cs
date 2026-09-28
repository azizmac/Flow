using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintStartCommand;

internal sealed class SprintStartCommandHandler(
    ISprintRepository sprints,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SprintStartCommand, SprintResponse?>
{
    public async Task<SprintResponse?> Handle(SprintStartCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var sprint = await sprints.GetByIdAsync(request.SprintId, cancellationToken);
        if (sprint is null)
            return null;

        permissions.EnsureCanManageSprints(await projectAccess.GetAsync(actor, sprint.BoardId, cancellationToken));

        // Один активный спринт на проект (§2): проверка здесь, частичный unique-индекс в БД страхует гонку.
        if (await sprints.HasActiveAsync(sprint.BoardId, sprint.Id, cancellationToken))
            throw new InvalidOperationException("В проекте уже идёт спринт — сначала завершите его.");

        sprint.Start(request.StartDate, request.EndDate, await tasks.GetBySprintIdAsync(sprint.Id, cancellationToken), DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sprint.ToResponse();
    }
}
