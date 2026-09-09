using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserListQuery;

internal sealed class UserListQueryHandler(IUserRepository users)
    : IRequestHandler<UserListQuery, IReadOnlyList<UserResponse>>
{
    public async Task<IReadOnlyList<UserResponse>> Handle(UserListQuery request, CancellationToken cancellationToken)
    {
        var all = await users.ListAsync(request.IncludeInactive, cancellationToken);
        return all.Select(u => u.ToResponse()).ToList();
    }
}
