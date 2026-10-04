using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserRemoveAvatarCommand;

/// <summary>
/// Убрать свой аватар — вернуть инициалы. Как и загрузка, только себе.
/// Обработчик - <see cref="UserRemoveAvatarCommandHandler"/>
/// </summary>
public sealed record UserRemoveAvatarCommand(Guid ActorId) : IRequest<UserResponse>;
