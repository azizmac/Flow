namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Связь с точки зрения задачи, для которой её запросили (docs/TZ_task_model.md §5): Outward — задача источник
/// («блокирует»), иначе цель («заблокирована»); у RelatesTo направления нет, Outward всегда true.
/// </summary>
public sealed record TaskLinkResponse(Guid Id, TaskLinkType Type, bool Outward, TaskLinkPeer Other, Guid CreatedById, DateTime CreatedAt);

/// <summary>
/// Вторая задача связи. Restricted — она в проекте, которого запросивший не видит: тогда известен только Id,
/// без кода и названия (связь при этом видна — её создал тот, кто видел обе задачи).
/// </summary>
public sealed record TaskLinkPeer(Guid Id, bool Restricted, string? Code = null, string? Title = null, Guid? BoardId = null, Guid? StatusId = null, bool IsDone = false);

/// <summary>Ответ на создание: CycleWarning — новая Blocks-связь замкнула цикл блокировок (не отказ, а предупреждение).</summary>
public sealed record TaskLinkCreatedResponse(TaskLinkResponse Link, bool CycleWarning);

/// <summary>Стрелка роадмапа: SourceId блокирует TargetId (обе задачи — в одном проекте).</summary>
public sealed record TaskBlockEdge(Guid SourceId, Guid TargetId);
