namespace Flow.Shared.Contracts.Tasks;

public sealed record AddChecklistItemRequest(string Text);

/// <summary>PATCH-семантика: null — не трогать.</summary>
public sealed record UpdateChecklistItemRequest(string? Text = null, bool? IsDone = null);

/// <summary>Полный список Id пунктов в новом порядке.</summary>
public sealed record ReorderChecklistRequest(IReadOnlyList<Guid> ItemIds);
