using MediatR;

namespace Flow.Application.Features.Users.Commands.UserActivateCommand;

/// <summary>Активация уже активного — InvalidOperationException (см. User.Activate) → 400.</summary>
public sealed record UserActivateCommand(Guid UserId) : IRequest<UserUpdateResult>;
