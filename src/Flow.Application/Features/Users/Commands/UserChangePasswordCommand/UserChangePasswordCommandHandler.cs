using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangePasswordCommand;

/// <summary>Пароль хранит только Flow.Auth — в Users ничего не меняется, поэтому SaveChanges не нужен.</summary>
internal sealed class UserChangePasswordCommandHandler(IUserRepository users, IAccountService accounts, ActorResolver actors, IPermissionService permissions)
    : IRequestHandler<UserChangePasswordCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanEditCredentials(actor, user);

        // Свой пароль меняется только с текущим; чужой (Owner) — сбрасывается, текущий не нужен и не проверяется.
        var isSelf = actor.Id == user.Id;
        if (isSelf && string.IsNullOrEmpty(request.CurrentPassword))
            throw new ArgumentException("Для смены своего пароля нужен текущий пароль.", nameof(request.CurrentPassword));

        var account = await accounts.ChangePasswordAsync(user.Id, isSelf ? request.CurrentPassword : null, request.NewPassword, cancellationToken);
        if (!account.IsSuccess)
            throw new ArgumentException(account.Error ?? "Flow.Auth rejected the password.", nameof(request.NewPassword));

        return UserUpdateResult.Success(user.ToResponse());
    }
}
