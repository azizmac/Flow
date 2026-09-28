namespace Flow.Application.Exceptions;

/// <summary>
/// Проект скрыт от actor'а (приватный, а он не участник) → 404, а не 403: ссылка с кодом задачи из приватного
/// проекта не должна подтверждать, что такая задача существует (docs/TZ_project_access.md, «Невидимое — 404»).
/// </summary>
public sealed class ProjectNotFoundException() : Exception("Не найдено.");
