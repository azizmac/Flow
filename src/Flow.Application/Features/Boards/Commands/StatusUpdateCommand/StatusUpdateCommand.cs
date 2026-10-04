using Flow.Shared.Contracts.Boards;
using MediatR;
using StatusType = Flow.Domain.Entities.StatusType;

namespace Flow.Application.Features.Boards.Commands.StatusUpdateCommand;

/// <summary>
/// Изменить статус (docs/TZ_workflow_config.md §1). PATCH-семантика: null — не трогать. IsInitial принимает только
/// true (флаг переносится на этот статус). Type задаёт вид, ClearType = true снимает его (статус без вида).
/// WipLimit — мягкий лимит колонки канбана, ClearWipLimit снимает его. Права — ManageConfig. Нарушение инварианта (последний финальный, начальный финальным) или занятое имя — 400.
/// Обработчик - <see cref="StatusUpdateCommandHandler"/>
/// </summary>
public sealed record StatusUpdateCommand(
    Guid ActorId,
    Guid BoardId,
    Guid StatusId,
    string? Name = null,
    bool? IsFinal = null,
    bool? IsInitial = null,
    StatusType? Type = null,
    bool ClearType = false,
    int? WipLimit = null,
    bool ClearWipLimit = false) : IRequest<BoardResponse?>;
