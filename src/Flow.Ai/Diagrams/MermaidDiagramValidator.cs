using System.Text.RegularExpressions;
using Flow.Ai.Agents;

namespace Flow.Ai.Diagrams;

/// <summary>
/// Структурный валидатор Mermaid-диаграммы (код, не LLM) — вход для конвертера
/// Mermaid → mxGraph XML (F18-F19, TZ_ai_local_assistant.md).
///
/// Сознательно принимает и 'flowchart', и 'graph' — в тестах модель устойчиво писала
/// 'graph TD' вместо явно запрошенного 'flowchart TD', и retry с точным указанием
/// на это не исправлял поведение за 3 попытки. Оба варианта — валидный Mermaid,
/// бороться с моделью за конкретный токен смысла нет: конвертер должен принимать оба.
/// </summary>
public sealed partial class MermaidDiagramValidator(int minNodes = 3) : IAgentValidator
{
    public ValidationResult Validate(string rawModelOutput)
    {
        var fences = FenceRegex().Matches(rawModelOutput);
        if (fences.Count != 1)
            return new ValidationResult(false, $"ожидался ровно 1 блок ```mermaid, найдено {fences.Count}");

        var body = fences[0].Groups[1].Value.Trim();
        var outside = FenceRegex().Replace(rawModelOutput, string.Empty).Trim();
        if (outside.Length > 0)
            return new ValidationResult(false, "есть текст вне блока ```mermaid");

        if (!DeclarationRegex().IsMatch(body))
            return new ValidationResult(false, "нет объявления graph/flowchart");

        var nodes = NodeRegex().Matches(body).Count;
        var edges = EdgeRegex().Matches(body).Count;

        if (nodes < minNodes)
            return new ValidationResult(false, $"слишком мало узлов ({nodes} < {minNodes})");

        if (body.Count(c => c == '[') != body.Count(c => c == ']') ||
            body.Count(c => c == '{') != body.Count(c => c == '}'))
            return new ValidationResult(false, "не сбалансированы скобки");

        return new ValidationResult(true, $"ok ({nodes} узлов, {edges} рёбер)");
    }

    [GeneratedRegex(@"```mermaid\s*(.*?)```", RegexOptions.Singleline)]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"(flowchart|graph)\s+(TD|LR|TB|RL)")]
    private static partial Regex DeclarationRegex();

    [GeneratedRegex(@"\w+[\[\{][^\]\}]+[\]\}]")]
    private static partial Regex NodeRegex();

    [GeneratedRegex(@"-->")]
    private static partial Regex EdgeRegex();
}
