using Flow.Application.Security;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetPreferencesQuery;

internal sealed class UserGetPreferencesQueryHandler(ActorResolver actors)
    : IRequestHandler<UserGetPreferencesQuery, UserPreferencesResponse>
{
    public async Task<UserPreferencesResponse> Handle(UserGetPreferencesQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        return actor.Preferences.ToResponse();
    }
}
