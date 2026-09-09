using Microsoft.JSInterop;

namespace Flow.Client.Services;

/// <summary>Геометрия якоря поповера (см. wwwroot/js/flow.js → flow.rect).</summary>
public sealed record AnchorRect(double Left, double Top, double Right, double Bottom, double Width, double Height, double Vw, double Vh);

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

    public async Task<AnchorRect?> RectAsync(string elementId)
    {
        try
        {
            return await js.InvokeAsync<AnchorRect?>("flow.rect", elementId);
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
