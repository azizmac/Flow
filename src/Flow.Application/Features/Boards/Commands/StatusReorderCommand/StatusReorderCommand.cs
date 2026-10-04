using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.StatusReorderCommand;

/// <summary>
/// Новый порядок статусов — полный список Id проекта (иначе 400). Права — ManageConfig.
/// Обработчик - <see cref="StatusReorderCommandHandler"/>
/// </summary>
public sealed record StatusReorderCommand(Guid ActorId, Guid BoardId, IReadOnlyList<Guid> StatusIds) : IRequest<BoardResponse?>;
