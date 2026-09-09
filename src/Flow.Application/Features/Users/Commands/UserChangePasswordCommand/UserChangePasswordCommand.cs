using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangePasswordCommand;

/// <summary>
/// CurrentPassword задан — смена своего пароля (Flow.Auth сверяет текущий); null — сброс без проверки.
/// Кто вправе сбрасывать чужой (Owner), решит IPermissionService (#18); до него право не проверяется.
/// Неверный текущий или слабый новый пароль → ArgumentException → 400.
/// </summary>
public sealed record UserChangePasswordCommand(Guid UserId, string? CurrentPassword, string NewPassword)
    : IRequest<UserUpdateResult>;
