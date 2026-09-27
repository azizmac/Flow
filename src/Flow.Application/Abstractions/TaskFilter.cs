using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Abstractions;

/// <summary>
/// Общее дерево условий на задачи (docs/TZ_task_views.md §7) — то, во что превращается FQL после биндинга имён.
/// Здесь только Id и значения, никаких имён: проект, статус и человек уже найдены. SQL из него строит Infrastructure
/// (TaskFilterTranslator). Отрицание — отдельным узлом с C#-семантикой null: «assignee != @ivan» включает задачи
/// без исполнителя, как ожидает человек.
/// </summary>
public abstract record TaskFilterNode;

public sealed record TaskFilterAnd(IReadOnlyList<TaskFilterNode> Items) : TaskFilterNode;

public sealed record TaskFilterOr(IReadOnlyList<TaskFilterNode> Items) : TaskFilterNode;

public sealed record TaskFilterNot(TaskFilterNode Item) : TaskFilterNode;

/// <summary>Поля-ссылки: значение задачи входит в набор Id.</summary>
public enum TaskFilterRef { Id, Board, Type, Status, Assignee, Creator, Parent, Sprint }

public sealed record TaskFilterIn(TaskFilterRef Field, IReadOnlyList<Guid> Ids) : TaskFilterNode;

/// <summary>Поля, которые бывают пустыми.</summary>
public enum TaskFilterNullable { Assignee, Parent, StartDate, DueDate, StoryPoints, Estimate, Links, Sprint }

public sealed record TaskFilterIsEmpty(TaskFilterNullable Field) : TaskFilterNode;

public sealed record TaskFilterTypeKinds(IReadOnlyList<TaskTypeKind> Kinds) : TaskFilterNode;

public sealed record TaskFilterStatusTypes(IReadOnlyList<StatusType> Types) : TaskFilterNode;

public enum TaskFilterScalar { Priority, Created, Updated, StartDate, DueDate, StoryPoints, Estimate }

public enum TaskFilterOp { Eq, Gt, Gte, Lt, Lte }

/// <summary>
/// Сравнение скаляра. Value: int (Priority, Estimate в минутах), decimal (StoryPoints), DateTime UTC (Created,
/// Updated), DateOnly (StartDate, DueDate). Равенство дате у Created/Updated биндер разворачивает в интервал суток.
/// </summary>
public sealed record TaskFilterCompare(TaskFilterScalar Field, TaskFilterOp Op, object Value) : TaskFilterNode;

/// <summary>Подстрока в названии, описании или коде (ILIKE), не семантический поиск.</summary>
public sealed record TaskFilterText(string Text) : TaskFilterNode;

/// <summary>Есть незакрытая задача, которая блокирует эту (то же, что TaskResponse.BlockedByCount &gt; 0).</summary>
public sealed record TaskFilterBlocked : TaskFilterNode;

/// <summary>Порядок из ORDER BY; первый — главный.</summary>
public sealed record TaskOrder(TaskSortField Field, bool Descending);
