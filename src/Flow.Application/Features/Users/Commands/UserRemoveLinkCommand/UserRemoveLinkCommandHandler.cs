using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;

internal sealed class UserRemoveLinkCommandHandler(IUserRepository users, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<UserRemoveLinkCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserRemoveLinkCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanEditProfile(actor, user);

        user.RemoveLink(request.Type.ToDomainLinkType());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
