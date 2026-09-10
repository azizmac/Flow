namespace Flow.Client.Services;

public enum ToastKind { Info, Ok, Error }

/// <summary>Leaving — тост доигрывает анимацию ухода перед удалением из списка.</summary>
public sealed record ToastMessage(int Id, string Text, ToastKind Kind)
{
    public bool Leaving { get; set; }
}

/// <summary>Очередь коротких уведомлений (ошибки API, «скопировано»). Рендерит Components/Toast.razor.</summary>
public sealed class ToastService
{
    private int _nextId;
    private readonly List<ToastMessage> _items = [];

    public IReadOnlyList<ToastMessage> Items => _items;

    public event Action? Changed;

    public void Show(string text, ToastKind kind = ToastKind.Info)
    {
        var toast = new ToastMessage(++_nextId, text, kind);
        _items.Add(toast);
        Changed?.Invoke();
        _ = RemoveLater(toast);
    }

    public void Error(string text) => Show(text, ToastKind.Error);

    public void Ok(string text) => Show(text, ToastKind.Ok);

    /// <summary>Длительность анимации ухода — держим в паре с .toast.out в app.css.</summary>
    private const int LeaveMs = 180;

    private async Task RemoveLater(ToastMessage toast)
    {
        await Task.Delay(toast.Kind == ToastKind.Error ? 5000 : 2500);
        toast.Leaving = true;
        Changed?.Invoke();

        await Task.Delay(LeaveMs);
        _items.Remove(toast);
        Changed?.Invoke();
    }
}
