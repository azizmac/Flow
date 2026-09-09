using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserSetLinkCommand;

/// <summary>Бросает ArgumentException, если URL не абсолютный http/https (см. UserLink.ValidateUrl).</summary>
internal sealed class UserSetLinkCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserSetLinkCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserSetLinkCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        user.SetLink(request.Type.ToDomainLinkType(), request.Url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
