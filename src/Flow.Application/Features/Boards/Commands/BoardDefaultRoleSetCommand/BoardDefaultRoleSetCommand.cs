using Flow.Shared.Contracts.Boards;
using MediatR;
using ProjectRole = Flow.Domain.Entities.ProjectRole;

namespace Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;

/// <summary>
/// Потолок роли в проекте без участия: Viewer, Member или Developer; null — снять. Admin — 400 (не имеет смысла).
/// Права — ManageMembers. Response = null, если проекта нет.
/// Обработчик - <see cref="BoardDefaultRoleSetCommandHandler"/>
/// </summary>
public sealed record BoardDefaultRoleSetCommand(Guid ActorId, Guid BoardId, ProjectRole? Role) : IRequest<BoardMembersResponse?>;
