using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeEmailCommand;

/// <summary>Result.ConflictError заполнен, если email (после нормализации) занят другим пользователем.</summary>
public sealed record UserChangeEmailCommand(Guid UserId, string Email) : IRequest<UserUpdateResult>;
