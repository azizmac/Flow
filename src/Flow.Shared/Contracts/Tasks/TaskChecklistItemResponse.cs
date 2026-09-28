namespace Flow.Shared.Contracts.Tasks;

/// <summary>Пункт чек-листа (docs/TZ_task_model.md §8); список приходит уже по порядку.</summary>
public sealed record TaskChecklistItemResponse(Guid Id, string Text, bool IsDone, DateTime? DoneAt, Guid? DoneById);
