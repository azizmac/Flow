using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserSearchQuery;

internal sealed class UserSearchQueryHandler(IUserRepository users)
    : IRequestHandler<UserSearchQuery, IReadOnlyList<UserResponse>>
{
    public async Task<IReadOnlyList<UserResponse>> Handle(UserSearchQuery request, CancellationToken cancellationToken)
    {
        var query = request.Query?.Trim();
        if (string.IsNullOrEmpty(query))
            return [];

        var limit = Math.Clamp(request.Limit, 1, UserSearchQuery.MaxLimit);
        var found = await users.SearchAsync(query, limit, cancellationToken);
        return found.Select(u => u.ToResponse()).ToList();
    }
}
