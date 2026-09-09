namespace Flow.Application.Abstractions;

/// <summary>
/// Кто совершает действие (actor) — пользователь из claim sub Bearer-токена Flow.Auth. Реализуется в Flow.Api
/// (ClaimsActorAccessor). null — запрос без токена: до fallback-политики такие запросы не доходят до контроллеров,
/// поэтому в командах actor считается обязательным. Права по ролям — IPermissionService (#18).
/// </summary>
public interface IActorAccessor
{
    Guid? ActorId { get; }
}
