using Flow.Shared.Contracts.Boards;
using MediatR;
using StatusType = Flow.Domain.Entities.StatusType;

namespace Flow.Application.Features.Boards.Commands.StatusCreateCommand;

/// <summary>
/// Добавить статус в конец списка (docs/TZ_workflow_config.md §1). Права — ManageConfig. Type — вид статуса для
/// фильтров во всех проектах, null — свой статус без вида. Занятое имя — 400. Response = null, если проекта нет.
/// </summary>
public sealed record StatusCreateCommand(Guid ActorId, Guid BoardId, string Name, StatusType? Type, bool IsFinal = false)
    : IRequest<BoardResponse?>;
