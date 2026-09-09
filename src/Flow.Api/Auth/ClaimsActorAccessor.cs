using System.Security.Claims;
using Flow.Application.Abstractions;

namespace Flow.Api.Auth;

/// <summary>
/// Actor из claim <c>sub</c> JWT (MapInboundClaims выключен, поэтому имя claim не переписывается в NameIdentifier;
/// на всякий случай читаем оба). Scoped: один HttpContext — один actor.
/// </summary>
public sealed class ClaimsActorAccessor(IHttpContextAccessor httpContextAccessor) : IActorAccessor
{
    public Guid? ActorId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }
}
