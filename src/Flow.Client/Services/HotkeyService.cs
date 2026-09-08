using Microsoft.JSInterop;

namespace Flow.Client.Services;

public sealed record HotkeyEvent(string Key, bool Ctrl, bool Shift, bool InEditable)
{
    public bool Handled { get; set; }
}

/// <summary>
/// Глобальные хоткеи (N — новая задача, Esc — закрыть, ↑/↓ — между задачами, Ctrl+Enter — создать).
/// JS-слушатель keydown вызывает OnKeyDown; подписчики (страницы/дровер) обрабатывают событие,
/// последний подписавшийся получает событие первым (дровер поверх страницы).
/// </summary>
public sealed class HotkeyService(IJSRuntime js) : IAsyncDisposable
{
    private readonly List<Func<HotkeyEvent, Task>> _handlers = [];
    private DotNetObjectReference<HotkeyService>? _self;
    private bool _registered;

    public async Task EnsureRegisteredAsync()
    {
        if (_registered)
            return;

        _self = DotNetObjectReference.Create(this);
        try
        {
            await js.InvokeVoidAsync("flow.registerHotkeys", _self);
            _registered = true;
        }
        catch (JSException)
        {
            // flow.js не загрузился — приложение работает без хоткеев
        }
    }

    public IDisposable Subscribe(Func<HotkeyEvent, Task> handler)
    {
        _handlers.Add(handler);
        return new Subscription(this, handler);
    }

    [JSInvokable]
    public async Task<bool> OnKeyDown(string key, bool ctrl, bool shift, bool inEditable)
    {
        var e = new HotkeyEvent(key, ctrl, shift, inEditable);
        for (var i = _handlers.Count - 1; i >= 0; i--)
        {
            await _handlers[i](e);
            if (e.Handled)
                return true;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_registered)
        {
            try
            {
                await js.InvokeVoidAsync("flow.unregisterHotkeys");
            }
            catch (JSException)
            {
            }
        }

        _self?.Dispose();
    }

    private sealed class Subscription(HotkeyService owner, Func<HotkeyEvent, Task> handler) : IDisposable
    {
        public void Dispose() => owner._handlers.Remove(handler);
    }
}
