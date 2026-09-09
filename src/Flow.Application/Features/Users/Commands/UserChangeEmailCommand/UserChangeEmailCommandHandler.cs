using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeEmailCommand;

internal sealed class UserChangeEmailCommandHandler(IUserRepository users, ActorResolver actors, IPermissionService permissions, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeEmailCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanEditCredentials(actor, user);

        // Как в UserChangeUsernameCommandHandler: нормализуем через домен, откатываем, проверяем, потом применяем.
        var previous = user.Email;
        user.ChangeEmail(request.Email);
        var candidate = user.Email;
        user.ChangeEmail(previous);

        if (candidate == previous)
            return UserUpdateResult.Success(user.ToResponse());

        if (await users.ExistsByEmailAsync(candidate, cancellationToken))
            return UserUpdateResult.EmailTaken(candidate);

        var account = await accounts.ChangeEmailAsync(user.Id, candidate, cancellationToken);
        if (account.Status == AccountResultStatus.Invalid)
            throw new ArgumentException(account.Error ?? "Flow.Auth rejected the email.", nameof(request.Email));

        if (!account.IsSuccess)
            return UserUpdateResult.EmailTaken(candidate);

        user.ChangeEmail(candidate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
