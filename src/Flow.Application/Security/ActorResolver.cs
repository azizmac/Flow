using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>
/// Загружает actor для изменяющей команды. Учётная запись в Flow.Auth может пережить профиль (удалён руками)
/// или быть деактивированной, пока не истёк access token, — оба случая здесь дают 401.
/// </summary>
internal sealed class ActorResolver(IUserRepository users)
{
    public async Task<User> ResolveAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new UnauthorizedActorException("Профиль для этой учётной записи не найден.");

        if (!actor.IsActive)
            throw new UnauthorizedActorException("Доступ закрыт: пользователь деактивирован.");

        return actor;
    }
}
