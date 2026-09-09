using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;

internal sealed class UserChangeUsernameCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeUsernameCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        // Нормализация живёт в домене, поэтому сначала применяем, потом сравниваем. Если username не изменился
        // (тот же после нормализации) — конфликта нет; если изменился и занят — откатываем и возвращаем 409.
        var previous = user.Username;
        user.ChangeUsername(request.Username);

        if (user.Username != previous && await users.ExistsByUsernameAsync(user.Username, cancellationToken))
        {
            var taken = user.Username;
            user.ChangeUsername(previous);
            return UserUpdateResult.UsernameTaken(taken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
