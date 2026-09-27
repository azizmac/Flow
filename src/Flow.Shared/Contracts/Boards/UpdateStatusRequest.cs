namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// PATCH-семантика: null — не трогать. IsInitial принимает только true (флаг переносится на этот статус).
/// Type задаёт вид, ClearType = true снимает его; вместе — 400.
/// </summary>
public sealed record UpdateStatusRequest(
    string? Name = null,
    bool? IsFinal = null,
    bool? IsInitial = null,
    StatusType? Type = null,
    bool ClearType = false);
