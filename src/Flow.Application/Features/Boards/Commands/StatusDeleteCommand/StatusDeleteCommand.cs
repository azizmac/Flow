using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.StatusDeleteCommand;

/// <summary>
/// Удалить статус, переведя его задачи в <paramref name="MoveToStatusId"/> (docs/TZ_workflow_config.md §1).
/// В журнал каждой переведённой задачи пишется StatusChanged от имени удалившего. Нельзя удалить начальный
/// и последний финальный — 400. Права — ManageConfig. Response = null, если проекта нет.
/// Обработчик - <see cref="StatusDeleteCommandHandler"/>
/// </summary>
public sealed record StatusDeleteCommand(Guid ActorId, Guid BoardId, Guid StatusId, Guid MoveToStatusId) : IRequest<BoardResponse?>;
