namespace Flow.Application.Features.Search;

/// <summary>
/// Шапка чанка — короткий контекст, который уходит <b>только в эмбеддер</b> и не пишется в Content.
/// Зачем: комментарий «да, согласен» сам по себе не найдётся ничем; с шапкой он оказывается рядом
/// с запросом про свой проект и свою задачу. В выдаче шапка не нужна — её собирает клиент из связей.
/// </summary>
public static class ChunkHeaderBuilder
{
    /// <summary>Задача: <c>[PROJ-142] Падает экспорт отчёта в PDF</c>.</summary>
    public static string ForTask(string taskCode, string title) =>
        Join($"[{taskCode}]", title.Trim(), " ");

    /// <summary>Комментарий: <c>Проект «Имя» · PROJ-142 · комментарий</c>.</summary>
    public static string ForComment(string boardName, string taskCode) =>
        Join(Project(boardName), taskCode, " · ") + " · комментарий";

    /// <summary>Проект: <c>Проект «Имя» (PROJ)</c>.</summary>
    public static string ForBoard(string boardName, string boardKey) =>
        Join(Project(boardName), $"({boardKey})", " ");

    /// <summary>Человек: <c>@username · Имя Фамилия</c>; без имени — только <c>@username</c>.</summary>
    public static string ForUser(string username, string? fullName) =>
        Join($"@{username}", fullName?.Trim(), " · ");

    private static string Project(string boardName) => $"Проект «{boardName.Trim()}»";

    private static string Join(string left, string? right, string separator) =>
        string.IsNullOrWhiteSpace(right) ? left : $"{left}{separator}{right}";
}
