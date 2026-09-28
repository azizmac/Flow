namespace Flow.Shared.Contracts.Boards;

/// <summary>Зеркало Domain.ScreenContext.</summary>
public enum ScreenContext
{
    Create = 0,
    Detail = 1
}

/// <summary>
/// Поле экрана (docs/TZ_workflow_config.md §3): «system:&lt;имя&gt;» (type, parent, sprint, milestone, priority, assignee,
/// start, due, estimate, description, checklist, links, attachments) или «custom:&lt;Id поля&gt;». Required — на Create
/// проверяет сервер, на Detail — подсветка; Section — заголовок группы перед полем.
/// </summary>
public sealed record ScreenFieldDto(string Field, bool Required = false, string? Section = null);

/// <summary>Настроенный экран; TaskTypeId = null — для всех типов. Ненастроенный — встроенный (все поля).</summary>
public sealed record TaskScreenResponse(Guid? TaskTypeId, ScreenContext Context, IReadOnlyList<ScreenFieldDto> Fields);

/// <summary>PUT /boards/{id}/screens/{typeId|default}/{context} — экран заменяется целиком.</summary>
public sealed record SetScreenRequest(IReadOnlyList<ScreenFieldDto> Fields);
