using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;

internal sealed class UserGetByUsernameQueryHandler(IUserRepository users)
    : IRequestHandler<UserGetByUsernameQuery, UserResponse?>
{
    public async Task<UserResponse?> Handle(UserGetByUsernameQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return null;

        var user = await users.GetByUsernameAsync(request.Username.Trim().ToLowerInvariant(), cancellationToken);
        return user?.ToResponse();
    }
}
