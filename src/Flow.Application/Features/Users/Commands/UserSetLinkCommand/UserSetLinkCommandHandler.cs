using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserSetLinkCommand;

/// <summary>Бросает ArgumentException, если URL не абсолютный http/https (см. UserLink.ValidateUrl).</summary>
internal sealed class UserSetLinkCommandHandler(IUserRepository users, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<UserSetLinkCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserSetLinkCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanEditProfile(actor, user);

        user.SetLink(request.Type.ToDomainLinkType(), request.Url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
