using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetMeQuery;

/// <summary>
/// Единственный запрос с побочным эффектом: первый вызов после входа переводит Invited → Active
/// (Flow.Auth о входах не сообщает, а клиент зовёт /users/me при каждом старте).
/// </summary>
internal sealed class UserGetMeQueryHandler(IUserRepository users, IUnitOfWork unitOfWork) : IRequestHandler<UserGetMeQuery, UserResponse?>
{
    public async Task<UserResponse?> Handle(UserGetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.ActorId, cancellationToken);
        if (user is null)
            return null;

        if (user.Status == UserStatus.Invited)
        {
            user.MarkActive();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return user.ToResponse();
    }
}
