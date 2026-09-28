using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;

/// <summary>
/// Убрать участие человека в проекте: его роль вернётся к роли по умолчанию. Права — ManageMembers.
/// 404 — нет проекта или человек не участник.
/// </summary>
public sealed record BoardMemberRemoveCommand(Guid ActorId, Guid BoardId, Guid UserId) : IRequest<BoardMemberResult>;
