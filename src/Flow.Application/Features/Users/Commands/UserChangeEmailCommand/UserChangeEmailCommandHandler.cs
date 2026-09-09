using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeEmailCommand;

internal sealed class UserChangeEmailCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserChangeEmailCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        var previous = user.Email;
        user.ChangeEmail(request.Email);

        if (user.Email != previous && await users.ExistsByEmailAsync(user.Email, cancellationToken))
        {
            var taken = user.Email;
            user.ChangeEmail(previous);
            return UserUpdateResult.EmailTaken(taken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
