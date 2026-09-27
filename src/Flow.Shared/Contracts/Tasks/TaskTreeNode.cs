namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Узел дерева задач (docs/TZ_task_model.md §3, docs/TZ_task_views.md §3): задачи идут плоским списком в порядке
/// обхода — родитель, затем его поддерево, внутри уровня по рангу; Depth — отступ (0 — верх дерева).
/// IsContextOnly — задача не проходит фильтр, но в её поддереве есть подходящие: показывается серым, чтобы было
/// видно, где подходящая задача живёт. VisibleChildCount — сколько детей узла прошло фильтр (с учётом их поддеревьев):
/// по нему клиент рисует стрелку раскрытия и понимает, что детей надо догрузить. Progress — всё поддерево без фильтра.
/// </summary>
public sealed record TaskTreeNode(
    TaskResponse Task,
    int Depth,
    bool IsContextOnly = false,
    int VisibleChildCount = 0,
    TaskTreeProgress? Progress = null);

/// <summary>Прогресс поддерева (без самой задачи): задач всего и закрыто, story points всего и в закрытых.</summary>
public sealed record TaskTreeProgress(int Total, int Done, decimal Points, decimal DonePoints);
