using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;

/// <summary>
/// Добавить человека в проект или сменить его роль (docs/TZ_project_access.md §4). Права — ManageMembers,
/// выдаваемая роль не выше своей. Деактивированного или несуществующего человека добавить нельзя (400).
/// </summary>
public sealed record BoardMemberSetCommand(Guid ActorId, Guid BoardId, Guid UserId, ProjectRole Role) : IRequest<BoardMemberResult>;
