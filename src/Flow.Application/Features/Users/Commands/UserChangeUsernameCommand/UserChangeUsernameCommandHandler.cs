using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;

internal sealed class UserChangeUsernameCommandHandler(IUserRepository users, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeUsernameCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        // Нормализация (trim + lower) живёт в домене: применяем, чтобы узнать итоговое значение, и сразу откатываем —
        // проверять занятость и ходить в Flow.Auth надо до того, как копия в Users изменится.
        var previous = user.Username;
        user.ChangeUsername(request.Username);
        var candidate = user.Username;
        user.ChangeUsername(previous);

        if (candidate == previous)
            return UserUpdateResult.Success(user.ToResponse());

        if (await users.ExistsByUsernameAsync(candidate, cancellationToken))
            return UserUpdateResult.UsernameTaken(candidate);

        // Источник истины — Flow.Auth: копия в Users меняется только после его согласия.
        var account = await accounts.ChangeUsernameAsync(user.Id, candidate, cancellationToken);
        if (account.Status == AccountResultStatus.Invalid)
            throw new ArgumentException(account.Error ?? "Flow.Auth rejected the username.", nameof(request.Username));

        if (!account.IsSuccess)
            return UserUpdateResult.UsernameTaken(candidate);

        user.ChangeUsername(candidate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
