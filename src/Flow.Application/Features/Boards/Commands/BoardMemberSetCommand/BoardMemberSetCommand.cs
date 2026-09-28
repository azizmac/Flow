using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;

/// <summary>
/// Добавить человека в проект или сменить его роль (docs/TZ_project_access.md §4). Права — ManageMembers,
/// выдаваемая роль не выше своей. Деактивированного или несуществующего человека добавить нельзя (400).
/// PermissionSetId (этап 4E) — свой набор прав вместо роли: роль тогда берётся базовая роль набора.
/// </summary>
public sealed record BoardMemberSetCommand(Guid ActorId, Guid BoardId, Guid UserId, ProjectRole Role, Guid? PermissionSetId = null) : IRequest<BoardMemberResult>;
