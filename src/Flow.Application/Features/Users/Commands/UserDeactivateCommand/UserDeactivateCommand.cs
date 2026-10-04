using MediatR;

namespace Flow.Application.Features.Users.Commands.UserDeactivateCommand;

/// <summary>
/// Повторная деактивация — InvalidOperationException (см. User.Deactivate) → 400.
/// Обработчик - <see cref="UserDeactivateCommandHandler"/>
/// </summary>
public sealed record UserDeactivateCommand(Guid ActorId, Guid UserId) : IRequest<UserUpdateResult>;
