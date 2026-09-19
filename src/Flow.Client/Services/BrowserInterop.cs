using Microsoft.JSInterop;

namespace Flow.Client.Services;

/// <summary>Значение и выделение textarea Markdown-редактора (flow.editor.state).</summary>
public sealed record EditorState(string Value, int Start, int End);

/// <summary>
/// Обёртка над wwwroot/js/flow.js: буфер обмена, фокус, редактор, приём файлов, выход.
/// Содержимое вложений сюда больше не попадает: и картинки, и скачивание — прямые ссылки /files/{id},
/// браузер забирает их сам.
/// </summary>
public sealed class BrowserInterop(IJSRuntime js)
{
    /// <summary>
    /// Отправляет POST на адрес хоста (выход). Именно POST: cookie сессии объявлена SameSite=Lax,
    /// и кросс-сайтовая форма её не донесёт — принудительный разлогин чужой страницей невозможен.
    /// </summary>
    public async Task PostFormAsync(string url)
    {
        await js.InvokeVoidAsync("flow.submitPost", url);
    }

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

    /// <summary>Возвращает фокус туда, где он был до открытия слоя (ловушка MudBlazor этого не делает).</summary>
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

    /// <summary>Создаёт редактор в контейнере. dotNetRef принимает ввод (HandleEditorInput) и клавиши (HandleEditorKeyAsync).</summary>
    /// <param name="hasCancel">Есть ли наверху обработчик отмены: от этого зависит, забирает ли редактор Escape.</param>
    public async Task<bool> EditorMountAsync<T>(string elementId, DotNetObjectReference<T> dotNetRef, string? value, string? placeholder, bool readOnly, bool hasCancel)
        where T : class
    {
        try
        {
            return await js.InvokeAsync<bool>("flowEditor.mount", elementId, dotNetRef,
                new { value, placeholder, readOnly, hasCancel });
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>
    /// Сообщает редактору, открыто ли меню упоминаний. Нужно потому, что решение «забрать клавишу себе»
    /// принимает JS синхронно (CodeMirror ждёт true/false), а спросить компонент синхронно нельзя:
    /// при серверном рендере он на другом конце SignalR.
    /// </summary>
    public async Task EditorSetMentionOpenAsync(string elementId, bool open)
    {
        try
        {
            await js.InvokeVoidAsync("flowEditor.setMentionOpen", elementId, open);
        }
        catch (JSException)
        {
            // редактор уже размонтирован
        }
    }

    /// <summary>Выполняет команду, которую компонент выбрал по клавише (обёртка, перенос, упоминание).</summary>
    public async Task EditorRunCommandAsync(string elementId, object command)
    {
        try
        {
            await js.InvokeVoidAsync("flowEditor.run", elementId, command);
        }
        catch (JSException)
        {
            // редактор уже размонтирован
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

    /// <summary>Ссылка тулбара и Ctrl K: [подпись](адрес) с выделением того, что осталось дописать.</summary>
    public async Task<string?> EditorLinkAsync(string elementId, string textPlaceholder)
    {
        try
        {
            return await js.InvokeAsync<string?>("flowEditor.link", elementId, textPlaceholder);
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
