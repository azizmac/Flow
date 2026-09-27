namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Связь от задачи из адреса ко второй — по Id или по коду (PROJ-142). Inward = true переворачивает направление:
/// «эта задача заблокирована второй» вместо «блокирует вторую». Для RelatesTo направления нет.
/// </summary>
public sealed record CreateTaskLinkRequest(TaskLinkType Type, Guid? TargetId = null, string? TargetCode = null, bool Inward = false);
