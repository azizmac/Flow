using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetMeQuery;

internal sealed class UserGetMeQueryHandler(IUserRepository users) : IRequestHandler<UserGetMeQuery, UserResponse?>
{
    public async Task<UserResponse?> Handle(UserGetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.ActorId, cancellationToken);
        return user?.ToResponse();
    }
}
