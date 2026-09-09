using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserDeactivateCommand;

/// <summary>
/// Сначала блокировка входа в Flow.Auth (иначе деактивированный продолжит входить и обновлять токены),
/// потом статус в Users. Если Flow.Auth недоступен — AuthUnavailableException, статус не меняется.
/// </summary>
internal sealed class UserDeactivateCommandHandler(IUserRepository users, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserDeactivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserDeactivateCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        if (!user.IsActive)
            throw new InvalidOperationException($"User {user.Id} is already deactivated.");

        await accounts.DisableAsync(user.Id, cancellationToken);

        user.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
