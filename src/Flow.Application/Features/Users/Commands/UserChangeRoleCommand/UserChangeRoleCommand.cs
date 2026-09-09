using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeRoleCommand;

/// <summary>
/// Права — IPermissionService.EnsureCanChangeRole (403). Последнего Owner понизить нельзя — InvalidOperationException (400).
/// Роль применяется сразу: она не в токене, а в Users (см. docs/TZ_auth.md).
/// </summary>
public sealed record UserChangeRoleCommand(Guid ActorId, Guid UserId, UserRole Role) : IRequest<UserUpdateResult>;
