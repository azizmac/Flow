using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskRankCommand;

/// <summary>
/// Переставить задачу в ручном порядке проекта (docs/TZ_task_model.md §7): после AfterId и/или перед BeforeId.
/// Хватает одного соседа — второй сервер берёт сам (ближайший по рангу). Ключ вычисляет сервер. Соседи — задачи
/// того же проекта, не сама задача; без соседей — 400. Права — как правка задачи. Журнал ранг не пишет и
/// UpdatedAt не двигает: это положение задачи, а не её изменение. Смена статуса и спринта перетаскиванием
/// появятся вместе с канбаном (2B) и бэклогом (2D).
/// </summary>
public sealed record TaskRankCommand(Guid ActorId, Guid TaskId, Guid? AfterId, Guid? BeforeId) : IRequest<TaskUpdateResult>;
