using System.Text.Json;
using Flow.Ai.Agents;

namespace Flow.Ai.Tasks;

/// <summary>
/// Детерминированная проверка + парсинг JSON-черновика задачи.
/// Проверяет только ФОРМАТ (валидный JSON, обязательные поля, допустимый priority) —
/// фактическую точность (существует ли такой project_key) валидатор не ловит,
/// это отдельная проверка на вызывающей стороне против реального списка проектов.
/// </summary>
public sealed class TaskDraftValidator : IAgentValidator
{
    private static readonly string[] AllowedPriorities = ["Low", "Medium", "High", "Urgent"];

    public ValidationResult Validate(string rawModelOutput)
    {
        var cleaned = Clean(rawModelOutput);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleaned);
        }
        catch (JsonException e)
        {
            return new ValidationResult(false, $"невалидный JSON: {e.Message}");
        }

        var root = doc.RootElement;
        foreach (var field in new[] { "title", "priority", "project_key" })
        {
            if (!root.TryGetProperty(field, out var prop) || prop.ValueKind != JsonValueKind.String)
                return new ValidationResult(false, $"отсутствует или неверный тип поля '{field}'");
        }

        var priority = root.GetProperty("priority").GetString();
        if (!AllowedPriorities.Contains(priority))
            return new ValidationResult(false, $"недопустимое значение priority: {priority}");

        return new ValidationResult(true, "ok");
    }

    public static TaskDraft Parse(string rawModelOutput)
    {
        var doc = JsonDocument.Parse(Clean(rawModelOutput));
        var root = doc.RootElement;
        return new TaskDraft(
            root.GetProperty("title").GetString()!,
            root.GetProperty("priority").GetString()!,
            root.GetProperty("project_key").GetString()!);
    }

    private static string Clean(string text)
    {
        var cleaned = text.Trim().Trim('`');
        if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[4..].Trim();
        return cleaned;
    }
}
