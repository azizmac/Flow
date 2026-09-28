using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.SprintReportQuery;

/// <summary>
/// Отчёт спринта (docs/TZ_task_views.md §2): взято на старте — снимок SprintCommitments; сделано — задачи спринта
/// в финальных статусах; не сделано — у завершённого снимок CarriedOver, у активного — незакрытые задачи спринта;
/// добавлено — сделанное и несделанное вне снимка старта. Burndown — story points незакрытых задач спринта на конец
/// каждого дня, восстановленные по журналу. null — спринта нет или проект скрыт.
/// </summary>
public sealed record SprintReportQuery(Guid ActorId, Guid SprintId) : IRequest<SprintReportResponse?>;
