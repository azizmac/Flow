namespace Flow.Shared.Contracts.Tasks;

// Слияние, разделение и перенос задачи (docs/TZ_task_model.md §6, этап 1E).

/// <summary>Влить задачу в Target: комментарии, вложения, связи и подзадачи переходят к Target, сама задача закрывается дублем.</summary>
public sealed record MergeTaskRequest(Guid TargetId);

public sealed record SplitPart(string Title, string? Description = null);

/// <summary>1–20 новых задач с тем же родителем, типом, приоритетом, исполнителем и полями; у каждой связь «выделена из».</summary>
public sealed record SplitTaskRequest(IReadOnlyList<SplitPart> Parts);

/// <summary>
/// Перенос в другой проект вместе с поддеревом. StatusMap/TypeMap — «Id в исходном проекте → Id в целевом»;
/// без записи статус подбирается по виду (иначе начальный), тип — по виду (иначе тип по умолчанию).
/// </summary>
public sealed record MoveTaskRequest(Guid BoardId, IReadOnlyDictionary<Guid, Guid>? StatusMap = null, IReadOnlyDictionary<Guid, Guid>? TypeMap = null);

/// <summary>Как отобразится статус или тип исходного проекта; TaskCount — сколько задач поддерева его несут.</summary>
public sealed record MoveMapping(Guid FromId, string FromName, Guid ToId, string ToName, int TaskCount);

/// <summary>Значение пользовательского поля, которое при переносе потеряется (в целевом проекте нет поля или варианта).</summary>
public sealed record LostFieldValue(string TaskCode, string FieldName);

/// <summary>
/// Что произойдёт при переносе — до подтверждения. Problems непусты — перенос откажет (например, в целевом проекте
/// нет типа ниже родителя для подзадачи). ParentDropped — у задачи есть родитель, и он останется в исходном проекте.
/// </summary>
public sealed record TaskMovePreviewResponse(
    Guid BoardId,
    int TaskCount,
    int AttachmentCount,
    bool ParentDropped,
    bool SprintReset,
    bool MilestoneReset,
    IReadOnlyList<MoveMapping> Statuses,
    IReadOnlyList<MoveMapping> Types,
    IReadOnlyList<LostFieldValue> LostFields,
    IReadOnlyList<string> Problems);
