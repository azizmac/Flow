namespace Flow.Ai.Agents;

/// <summary>
/// Детерминированный валидатор ответа модели — код, не LLM.
/// Каждый вызов GeneratorAgent проходит через свой валидатор перед тем,
/// как результат будет принят или отправлен модели на повтор.
/// </summary>
public interface IAgentValidator
{
    ValidationResult Validate(string rawModelOutput);
}
