using Microsoft.JSInterop;

namespace Flow.Client.Services;

/// <summary>
/// Геометрия якоря поповера (см. wwwroot/js/flow.js → flow.rect). OriginX/OriginY — начало
/// координат fixed-слоя (скрима): не ноль, если слой лежит внутри предка с transform.
/// </summary>
public sealed record AnchorRect(
    double Left, double Top, double Right, double Bottom, double Width, double Height,
    double Vw, double Vh, double OriginX, double OriginY);

/// <summary>Значение и выделение textarea Markdown-редактора (flow.editor.state).</summary>
public sealed record EditorState(string Value, int Start, int End);

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

    // ---- Markdown-редактор (flowEditor.*, CodeMirror): все методы возвращают новое значение или null. ----

    /// <summary>Подгружает бандл редактора (один раз на страницу). false — не загрузился, поля ввода не будет.</summary>
    public async Task<bool> EnsureEditorLoadedAsync()
    {
        try
        {
            return await js.InvokeAsync<bool>("flow.loadEditor");
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>Создаёт редактор в контейнере. dotNetRef принимает ввод (HandleEditorInput) и клавиши (HandleEditorKey).</summary>
    public async Task<bool> EditorMountAsync<T>(string elementId, DotNetObjectReference<T> dotNetRef, string? value, string? placeholder, bool readOnly)
        where T : class
    {
        try
        {
            return await js.InvokeAsync<bool>("flowEditor.mount", elementId, dotNetRef,
                new { value, placeholder, readOnly });
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task EditorDestroyAsync(string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("flowEditor.destroy", elementId);
        }
        catch (JSException)
        {
        }
    }

    public async Task EditorFocusAsync(string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("flowEditor.focus", elementId);
        }
        catch (JSException)
        {
        }
    }

    public async Task EditorSetReadOnlyAsync(string elementId, bool readOnly)
    {
        try
        {
            await js.InvokeVoidAsync("flowEditor.setReadOnly", elementId, readOnly);
        }
        catch (JSException)
        {
        }
    }

    public async Task<string?> EditorSetValueAsync(string elementId, string value)
    {
        try
        {
            return await js.InvokeAsync<string?>("flowEditor.setValue", elementId, value);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task<EditorState?> EditorStateAsync(string elementId)
    {
        try
        {
            return await js.InvokeAsync<EditorState?>("flowEditor.state", elementId);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task<string?> EditorWrapAsync(string elementId, string before, string after, string placeholder)
    {
        try
        {
            return await js.InvokeAsync<string?>("flowEditor.wrap", elementId, before, after, placeholder);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task<string?> EditorPrefixLinesAsync(string elementId, string prefix, bool ordered = false)
    {
        try
        {
            return await js.InvokeAsync<string?>("flowEditor.prefixLines", elementId, prefix, ordered);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>
    /// Вставляет текст на месте курсора (ссылка на вложение). Своей функции в бандле редактора нет:
    /// это wrap без «середины» — каретка встаёт сразу за вставленным. Пересобирать CodeMirror
    /// (полтысячи килобайт, отдельная сборка) ради одного частного случая дороже, чем этот вызов.
    /// </summary>
    public Task<string?> EditorInsertAsync(string elementId, string text) =>
        EditorWrapAsync(elementId, text, string.Empty, string.Empty);

    public async Task<string?> EditorInsertMentionAsync(string elementId, int atPosition, string username)
    {
        try
        {
            return await js.InvokeAsync<string?>("flowEditor.insertMention", elementId, atPosition, username);
        }
        catch (JSException)
        {
            return null;
        }
    }

    // ---- Вложения: байты из .NET превращаются в blob, потому что прямой ссылки на файл нет. ----

    /// <summary>blob:-URL для превью картинки. null — браузер не дал создать объект. Освобождать через RevokeBlobUrlAsync.</summary>
    public async Task<string?> BlobUrlAsync(string contentType, byte[] bytes)
    {
        try
        {
            return await js.InvokeAsync<string?>("flow.blobUrl", contentType, bytes);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task RevokeBlobUrlAsync(string url)
    {
        try
        {
            await js.InvokeVoidAsync("flow.revokeBlobUrl", url);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>
    /// Делает элемент зоной приёма файлов: брошенное (а с acceptPaste — и вставленное из буфера)
    /// попадает в скрытый input, который читает InputFile. Прямого доступа к DataTransfer из WASM нет.
    /// </summary>
    public async Task AttachZoneAsync(string zoneId, string inputId, bool acceptPaste)
    {
        try
        {
            await js.InvokeAsync<bool>("flow.attachZone", zoneId, inputId, acceptPaste);
        }
        catch (JSException)
        {
        }
    }

    public async Task DetachZoneAsync(string zoneId)
    {
        try
        {
            await js.InvokeVoidAsync("flow.detachZone", zoneId);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Оживляет ссылки на вложения внутри отрендеренного Markdown (см. MarkdownView).</summary>
    public async Task HydrateAttachmentsAsync<T>(string containerId, DotNetObjectReference<T> dotNetRef) where T : class
    {
        try
        {
            await js.InvokeVoidAsync("flow.hydrateAttachments", containerId, dotNetRef);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Очищает выбор в input type=file — иначе тот же файл второй раз не выберешь.</summary>
    public async Task ResetFileInputAsync(string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("flow.resetFileInput", elementId);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>Отдаёт файл браузеру на скачивание. false — заблокировано (например, всплывающие окна).</summary>
    public async Task<bool> SaveFileAsync(string fileName, string contentType, byte[] bytes)
    {
        try
        {
            return await js.InvokeAsync<bool>("flow.saveFile", fileName, contentType, bytes);
        }
        catch (JSException)
        {
            return false;
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
