using Flow.Application.Exceptions;

namespace Flow.Application.Abstractions;

public static class ActorAccessorExtensions
{
    /// <summary>Id actor'а для изменяющих команд. Нет sub в токене → <see cref="UnauthorizedActorException"/> → 401.</summary>
    public static Guid Require(this IActorAccessor accessor) =>
        accessor.ActorId ?? throw new UnauthorizedActorException("В токене нет идентификатора пользователя.");
}
