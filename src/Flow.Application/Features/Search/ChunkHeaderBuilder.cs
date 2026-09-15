namespace Flow.Application.Features.Search;

/// <summary>
/// Контекстная шапка чанка. Уходит только в эмбеддер и в БД не дублируется: подсветка и полнотекстовый
/// поиск работают по чистому Content, а вектор получает контекст («PROJ-142», имя проекта, @username) —
/// без него чанк описания «не воспроизводится» неотличим от тысячи таких же.
/// </summary>
public static class ChunkHeaderBuilder
{
    /// <summary>Задача: <c>[PROJ-142] Падает экспорт отчёта в PDF</c>.</summary>
    public static string ForTask(string taskCode, string title) => $"[{taskCode}] {title}".Trim();

    /// <summary>Комментарий: <c>Проект «Имя» · PROJ-142 · комментарий</c>.</summary>
    public static string ForComment(string boardName, string taskCode) =>
        $"Проект «{boardName}» · {taskCode} · комментарий";

    /// <summary>Проект: <c>Проект «Имя» (PROJ)</c>.</summary>
    public static string ForBoard(string name, string key) => $"Проект «{name}» ({key})";

    /// <summary>Человек: <c>@username · Имя Фамилия</c>; без имени — только @username.</summary>
    public static string ForUser(string username, string? firstName, string? lastName)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return fullName.Length == 0 ? $"@{username}" : $"@{username} · {fullName}";
    }

    /// <summary>Что реально уходит в эмбеддер: шапка, перевод строки, текст чанка.</summary>
    public static string Apply(string header, string content) =>
        header.Length == 0 ? content : $"{header}\n{content}";
}
