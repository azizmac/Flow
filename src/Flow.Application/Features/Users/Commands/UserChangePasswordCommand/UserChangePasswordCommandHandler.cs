using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangePasswordCommand;

/// <summary>Пароль хранит только Flow.Auth — в Users ничего не меняется, поэтому SaveChanges не нужен.</summary>
internal sealed class UserChangePasswordCommandHandler(IUserRepository users, IAccountService accounts)
    : IRequestHandler<UserChangePasswordCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        var account = await accounts.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (!account.IsSuccess)
            throw new ArgumentException(account.Error ?? "Flow.Auth rejected the password.", nameof(request.NewPassword));

        return UserUpdateResult.Success(user.ToResponse());
    }
}
