using System.Net;
using Flow.Shared.Contracts.Search;
using Microsoft.AspNetCore.Components;

namespace Flow.Client.Services;

/// <summary>Общее для строки поиска и страницы выдачи: подсветка, подписи типов и ссылки на источник.</summary>
public static class SearchPresentation
{
    /// <summary>
    /// Фрагмент из ts_headline. Сервер не экранирует текст источника — он экранируется здесь,
    /// и обратно разворачиваются только собственные теги подсветки. Иначе описание задачи с
    /// «&lt;script&gt;» приехало бы в DOM как разметка.
    /// </summary>
    public static MarkupString Highlight(string? snippet) =>
        new(WebUtility.HtmlEncode(snippet ?? string.Empty)
            .Replace("&lt;mark&gt;", "<mark>", StringComparison.Ordinal)
            .Replace("&lt;/mark&gt;", "</mark>", StringComparison.Ordinal));

    public static string TypeLabel(SearchSourceType type) => type switch
    {
        SearchSourceType.Task => "Задача",
        SearchSourceType.Comment => "Комментарий",
        SearchSourceType.Board => "Проект",
        SearchSourceType.User => "Человек",
        _ => "Источник"
    };

    /// <summary>Множественное число для заголовков групп в выдаче.</summary>
    public static string GroupLabel(SearchSourceType type) => type switch
    {
        SearchSourceType.Task => "Задачи",
        SearchSourceType.Comment => "Комментарии",
        SearchSourceType.Board => "Проекты",
        SearchSourceType.User => "Люди",
        _ => "Источники"
    };

    public static string TypeIcon(SearchSourceType type) => type switch
    {
        SearchSourceType.Task => "check",
        SearchSourceType.Comment => "message",
        SearchSourceType.Board => "grid",
        SearchSourceType.User => "user",
        _ => "file"
    };

    /// <summary>
    /// Куда ведёт результат. У комментария своей страницы нет — открывается его задача
    /// (ParentId), поэтому без него результат некликабелен.
    /// </summary>
    public static string? Href(SearchResultItem item) => item.SourceType switch
    {
        SearchSourceType.Task => $"tasks/{item.SourceId}",
        SearchSourceType.Comment => item.ParentId is { } taskId ? $"tasks/{taskId}" : null,
        SearchSourceType.Board => $"boards/{item.SourceId}",
        SearchSourceType.User => $"users/{item.SourceId}",
        _ => null
    };
}
