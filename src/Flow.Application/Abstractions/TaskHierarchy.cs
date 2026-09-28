using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Прямые подзадачи: всего и в финальном статусе — для прогресса «3/5» (docs/TZ_task_model.md §3).</summary>
public readonly record struct ChildCounts(int Total, int Done);

/// <summary>Узел дерева задач: задача и глубина от корня обхода (0 — верхний уровень или сам запрошенный корень).</summary>
public sealed record TaskTreeEntry(TaskItem Task, int Depth);
