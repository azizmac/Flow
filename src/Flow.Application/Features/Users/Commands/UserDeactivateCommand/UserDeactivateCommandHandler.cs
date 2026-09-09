using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserDeactivateCommand;

internal sealed class UserDeactivateCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserDeactivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserDeactivateCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        user.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
