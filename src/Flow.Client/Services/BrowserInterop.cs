using Microsoft.JSInterop;

namespace Flow.Client.Services;

/// <summary>
/// Геометрия якоря поповера (см. wwwroot/js/flow.js → flow.rect). OriginX/OriginY — начало
/// координат fixed-слоя (скрима): не ноль, если слой лежит внутри предка с transform.
/// </summary>
public sealed record AnchorRect(
    double Left, double Top, double Right, double Bottom, double Width, double Height,
    double Vw, double Vh, double OriginX, double OriginY);

/// <summary>Обёртка над wwwroot/js/flow.js: буфер обмена, фокус, геометрия элементов, localStorage.</summary>
public sealed class BrowserInterop(IJSRuntime js)
{
    public async Task<bool> CopyAsync(string text)
    {
        try
        {
            return await js.InvokeAsync<bool>("flow.copy", text);
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task FocusAsync(string elementId, bool select = false)
    {
        try
        {
            await js.InvokeVoidAsync("flow.focus", elementId, select);
        }
        catch (JSException)
        {
            // элемент мог исчезнуть между рендерами — не критично
        }
    }

    public async Task<AnchorRect?> RectAsync(string elementId, string? layerId = null)
    {
        try
        {
            return await js.InvokeAsync<AnchorRect?>("flow.rect", elementId, layerId);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task<string?> StorageGetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("flow.storageGet", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task StorageSetAsync(string key, string? value)
    {
        try
        {
            await js.InvokeVoidAsync("flow.storageSet", key, value);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Запоминает текущий фокус перед открытием модального слоя.</summary>
    public async Task PushFocusAsync()
    {
        try
        {
            await js.InvokeVoidAsync("flow.pushFocus");
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Возвращает фокус туда, откуда слой открыли.</summary>
    public async Task PopFocusAsync()
    {
        try
        {
            await js.InvokeVoidAsync("flow.popFocus");
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Ставит фокус на первый интерактивный элемент внутри контейнера (CSS-селектор).</summary>
    public async Task FocusFirstInAsync(string selector)
    {
        try
        {
            await js.InvokeVoidAsync("flow.focusFirstIn", selector);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Двигает фокус по пунктам меню внутри контейнера: first | last | next | prev.</summary>
    public async Task MenuFocusAsync(string containerId, string mode)
    {
        try
        {
            await js.InvokeVoidAsync("flow.menuFocus", containerId, mode);
        }
        catch (JSException)
        {
        }
    }

    public async Task ScrollIntoViewAsync(string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("flow.scrollIntoView", elementId);
        }
        catch (JSException)
        {
        }
    }
}
