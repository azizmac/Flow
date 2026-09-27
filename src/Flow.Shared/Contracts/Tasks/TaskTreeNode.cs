namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Узел дерева задач (docs/TZ_task_model.md §3): задачи идут плоским списком в порядке обхода — родитель, затем
/// его поддерево, внутри уровня по рангу; Depth — отступ (0 — верх дерева).
/// </summary>
public sealed record TaskTreeNode(TaskResponse Task, int Depth);
