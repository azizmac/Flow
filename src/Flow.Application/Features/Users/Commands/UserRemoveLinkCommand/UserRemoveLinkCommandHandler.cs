using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;

internal sealed class UserRemoveLinkCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserRemoveLinkCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserRemoveLinkCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        user.RemoveLink(request.Type.ToDomainLinkType());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
