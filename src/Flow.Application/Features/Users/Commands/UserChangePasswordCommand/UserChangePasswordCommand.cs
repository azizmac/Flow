using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangePasswordCommand;

/// <summary>
/// Свой пароль: CurrentPassword обязателен (Flow.Auth сверяет). Чужой — только Owner, сброс без текущего
/// (IPermissionService.EnsureCanEditCredentials → 403).
/// Неверный текущий или слабый новый пароль → ArgumentException → 400.
/// </summary>
public sealed record UserChangePasswordCommand(Guid ActorId, Guid UserId, string? CurrentPassword, string NewPassword)
    : IRequest<UserUpdateResult>;
