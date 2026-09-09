using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeRoleCommand;

internal sealed class UserChangeRoleCommandHandler(
    IUserRepository users,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeRoleCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeRoleCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var target = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (target is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanChangeRole(actor, target, request.Role);

        if (target.Role == request.Role)
            return UserUpdateResult.Success(target.ToResponse());

        // В workspace всегда минимум один Owner — домен этого не видит, проверяем здесь.
        if (target.Role == UserRole.Owner && await users.CountByRoleAsync(UserRole.Owner, cancellationToken) <= 1)
            throw new InvalidOperationException("Нельзя понизить последнего Owner: сначала назначьте другого.");

        target.ChangeRole(request.Role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(target.ToResponse());
    }
}
