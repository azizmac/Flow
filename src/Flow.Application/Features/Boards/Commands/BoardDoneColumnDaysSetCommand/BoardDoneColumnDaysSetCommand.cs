using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;

/// <summary>
/// Окно финальной колонки канбана в днях (docs/TZ_task_views.md §1), 1…365. Это настройка проекта, как типы задач
/// и workflow, поэтому право — ManageConfig. Вне диапазона — 400.
/// </summary>
public sealed record BoardDoneColumnDaysSetCommand(Guid ActorId, Guid BoardId, int Days) : IRequest<BoardResponse?>;
