namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Новое место задачи в ручном порядке: после <see cref="AfterId"/> и/или перед <see cref="BeforeId"/>
/// (задачи того же проекта). Ключ ранга вычисляет сервер; хотя бы один сосед обязателен.
/// </summary>
public sealed record RankTaskRequest(Guid? AfterId, Guid? BeforeId);
