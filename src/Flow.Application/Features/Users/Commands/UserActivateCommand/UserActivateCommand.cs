using MediatR;

namespace Flow.Application.Features.Users.Commands.UserActivateCommand;

/// <summary>
/// Активация уже активного — InvalidOperationException (см. User.Activate) → 400.
/// Обработчик - <see cref="UserActivateCommandHandler"/>
/// </summary>
public sealed record UserActivateCommand(Guid ActorId, Guid UserId) : IRequest<UserUpdateResult>;
