using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetQuery;

internal sealed class UserGetQueryHandler(IUserRepository users) : IRequestHandler<UserGetQuery, UserResponse?>
{
    public async Task<UserResponse?> Handle(UserGetQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        return user?.ToResponse();
    }
}
