using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserActivateCommand;

internal sealed class UserActivateCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserActivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserActivateCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        user.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
