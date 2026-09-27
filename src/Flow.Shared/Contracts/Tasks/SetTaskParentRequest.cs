namespace Flow.Shared.Contracts.Tasks;

/// <summary>null — сделать задачу самостоятельной. Родитель — из того же проекта и выше по уровню типа.</summary>
public sealed record SetTaskParentRequest(Guid? ParentId);
