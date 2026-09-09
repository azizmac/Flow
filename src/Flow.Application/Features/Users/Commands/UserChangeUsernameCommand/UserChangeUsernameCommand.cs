using MediatR;

namespace Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;

/// <summary>Result.ConflictError заполнен, если username (после нормализации) занят другим пользователем.</summary>
public sealed record UserChangeUsernameCommand(Guid ActorId, Guid UserId, string Username) : IRequest<UserUpdateResult>;
