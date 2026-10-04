using Flow.Shared.Contracts.Boards;
using MediatR;
using BoardVisibility = Flow.Domain.Entities.BoardVisibility;

namespace Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;

/// <summary>
/// Сделать проект приватным или открытым (docs/TZ_project_access.md, этап 4B). Права — ManageMembers.
/// Приватный проект без участников-администраторов допустим: его всё равно видят глобальные Admin/Owner,
/// а предупреждает об этом диалог. Response = null, если проекта нет.
/// Обработчик - <see cref="BoardVisibilitySetCommandHandler"/>
/// </summary>
public sealed record BoardVisibilitySetCommand(Guid ActorId, Guid BoardId, BoardVisibility Visibility) : IRequest<BoardMembersResponse?>;
