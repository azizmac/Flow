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
public enum TaskFilterRef { Id, Board, Type, Status, Assignee, Creator, Parent, Sprint, Milestone }

public sealed record TaskFilterIn(TaskFilterRef Field, IReadOnlyList<Guid> Ids) : TaskFilterNode;

/// <summary>Поля, которые бывают пустыми.</summary>
public enum TaskFilterNullable { Assignee, Parent, StartDate, DueDate, StoryPoints, Estimate, Links, Sprint, Milestone }

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

public enum TaskFilterPullRequestState { Open, Merged, None }

/// <summary>
/// Pull request'ы задачи (FQL development, docs/TZ_scm_integration.md §7): Open — есть открытый или черновой PR,
/// Merged — есть смёрженный, None — ни одного PR.
/// </summary>
public sealed record TaskFilterPullRequest(TaskFilterPullRequestState State) : TaskFilterNode;

/// <summary>Операция над пользовательским полем: сравнение, подстрока (~), «одно из» (Id вариантов и людей), пусто.</summary>
public enum TaskFilterCustomOp { Eq, Gt, Gte, Lt, Lte, Contains, Any, IsEmpty }

/// <summary>
/// Условие на пользовательское поле `cf.&lt;key&gt;` (docs/TZ_task_model.md §4). FieldIds — поля с этим ключом во всех
/// видимых проектах (у каждого проекта своё поле, тип у них один — это проверяет биндер). Value: string — текст
/// (Eq без учёта регистра, Contains — подстрока) и "true"/"false" у флажка; decimal — число; DateOnly — дата;
/// IReadOnlyList&lt;string&gt; — Id для Any; null — IsEmpty.
/// </summary>
public sealed record TaskFilterCustomField(IReadOnlyList<Guid> FieldIds, CustomFieldType Type, TaskFilterCustomOp Op, object? Value) : TaskFilterNode;

/// <summary>Порядок из ORDER BY; первый — главный.</summary>
public sealed record TaskOrder(TaskSortField Field, bool Descending);
