using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserActivateCommand;

/// <summary>Зеркало деактивации: сначала снять блокировку входа в Flow.Auth, потом статус в Users.</summary>
internal sealed class UserActivateCommandHandler(IUserRepository users, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserActivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserActivateCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        if (user.IsActive)
            throw new InvalidOperationException($"User {user.Id} is already active.");

        await accounts.EnableAsync(user.Id, cancellationToken);

        user.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
