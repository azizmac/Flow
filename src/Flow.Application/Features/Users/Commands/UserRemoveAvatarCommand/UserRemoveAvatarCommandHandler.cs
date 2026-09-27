using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserRemoveAvatarCommand;

/// <summary>Сначала строка, потом объект — как при удалении вложения: картинка без ссылки на неё лучше ссылки в никуда.</summary>
internal sealed class UserRemoveAvatarCommandHandler(ActorResolver actors, IFileStorage storage, IUnitOfWork unitOfWork)
    : IRequestHandler<UserRemoveAvatarCommand, UserResponse>
{
    public async Task<UserResponse> Handle(UserRemoveAvatarCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var previous = actor.UploadedAvatarFileName();
        if (!actor.RemoveAvatar())
            return actor.ToResponse();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await UserAvatars.SafeDeleteAsync(storage, actor.Id, previous, cancellationToken);

        return actor.ToResponse();
    }
}
