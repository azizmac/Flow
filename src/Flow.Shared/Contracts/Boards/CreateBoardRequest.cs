namespace Flow.Shared.Contracts.Boards;

/// <summary>TemplateId — встроенный или сохранённый шаблон проекта (этап 3F); null — «Простой», как раньше.</summary>
public sealed record CreateBoardRequest(string Name, string Key, Guid? TemplateId = null);
