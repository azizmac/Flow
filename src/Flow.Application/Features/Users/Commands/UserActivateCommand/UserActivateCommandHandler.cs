using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserActivateCommand;

/// <summary>Зеркало деактивации: сначала снять блокировку входа в Flow.Auth, потом статус в Users.</summary>
internal sealed class UserActivateCommandHandler(IUserRepository users, ActorResolver actors, IPermissionService permissions, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserActivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserActivateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanDeactivate(actor);

        if (user.IsActive)
            throw new InvalidOperationException($"User {user.Id} is already active.");

        await accounts.EnableAsync(user.Id, cancellationToken);

        user.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
