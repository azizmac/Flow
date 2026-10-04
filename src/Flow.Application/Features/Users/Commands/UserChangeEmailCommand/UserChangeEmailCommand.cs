using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeEmailCommand;

/// <summary>
/// Result.ConflictError заполнен, если email (после нормализации) занят другим пользователем.
/// Обработчик - <see cref="UserChangeEmailCommandHandler"/>
/// </summary>
public sealed record UserChangeEmailCommand(Guid ActorId, Guid UserId, string Email) : IRequest<UserUpdateResult>;
