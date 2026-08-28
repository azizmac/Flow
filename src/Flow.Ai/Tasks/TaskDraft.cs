namespace Flow.Ai.Tasks;

/// <summary>
/// Черновик задачи, извлечённый моделью из свободного текста или из раздела
/// «Этапы внедрения» сгенерированного ТЗ. Не создаёт TaskItem напрямую —
/// только предлагает поля; финальное создание — на вызывающей стороне,
/// после подтверждения пользователем (F14-F16, TZ_ai_local_assistant.md).
/// </summary>
public sealed record TaskDraft(string Title, string Priority, string ProjectKey);
