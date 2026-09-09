using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;

/// <summary>Бросает ArgumentException при невалидном имени/телефоне/URL (см. методы User.Change*).</summary>
internal sealed class UserUpdateProfileCommandHandler(IUserRepository users, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<UserUpdateProfileCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserUpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanEditProfile(actor, user);

        if (request.FirstName is not null || request.LastName is not null)
            user.ChangeName(request.FirstName ?? user.FirstName, request.LastName ?? user.LastName);

        if (request.JobTitle is not null)
            user.ChangeJobTitle(request.JobTitle);

        if (request.Bio is not null)
            user.ChangeBio(request.Bio);

        if (request.PhoneNumber is not null)
            user.ChangePhoneNumber(request.PhoneNumber);

        if (request.AvatarUrl is not null)
            user.ChangeAvatar(request.AvatarUrl);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
