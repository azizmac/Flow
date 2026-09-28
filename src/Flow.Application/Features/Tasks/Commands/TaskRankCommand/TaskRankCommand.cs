using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskRankCommand;

/// <summary>
/// Переставить задачу в ручном порядке проекта (docs/TZ_task_model.md §7): после AfterId и/или перед BeforeId.
/// Хватает одного соседа — второй сервер берёт сам (ближайший по рангу). Ключ вычисляет сервер. Соседи — задачи
/// того же проекта, не сама задача. Права — как правка задачи. Журнал ранг не пишет и UpdatedAt не двигает: это
/// положение задачи, а не её изменение.
/// StatusId — перенос в другую колонку канбана (docs/TZ_task_views.md §1): переход проверяет workflow
/// (TransitionGuard, отказ — TransitionNotAllowed), в журнал идёт StatusChanged, в очередь поиска — Upsert.
/// SprintId / ToBacklog — перенос между секциями бэклога (docs/TZ_task_views.md §2): спринт своего проекта, не
/// завершённый, SprintChanged в журнале. Со StatusId или сменой спринта соседи необязательны (пустая секция — ранг
/// не меняется); без них и без соседей — 400.
/// </summary>
public sealed record TaskRankCommand(
    Guid ActorId,
    Guid TaskId,
    Guid? AfterId,
    Guid? BeforeId,
    Guid? StatusId = null,
    Guid? SprintId = null,
    bool ToBacklog = false) : IRequest<TaskUpdateResult>;
