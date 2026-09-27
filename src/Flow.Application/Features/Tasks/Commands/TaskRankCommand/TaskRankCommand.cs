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
/// Со StatusId соседи необязательны (пустая колонка — ранг не меняется); без него и без соседей — 400.
/// </summary>
public sealed record TaskRankCommand(Guid ActorId, Guid TaskId, Guid? AfterId, Guid? BeforeId, Guid? StatusId = null) : IRequest<TaskUpdateResult>;
