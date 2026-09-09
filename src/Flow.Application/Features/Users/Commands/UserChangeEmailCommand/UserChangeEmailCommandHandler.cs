using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeEmailCommand;

internal sealed class UserChangeEmailCommandHandler(IUserRepository users, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeEmailCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

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
