using Flow.Ai.Client;
using Flow.Ai.Options;
using Microsoft.Extensions.Options;

namespace Flow.Ai.Agents;

/// <summary>
/// Generator + Validator + retry-critic. Паттерн проверен эмпирически на Qwen2.5-3B/7B:
/// генерация всегда проходит через детерминированный валидатор; если тот не согласен,
/// его причина отправляется модели как сообщение критика, и генерация повторяется
/// до MaxAttempts раз. Ничего не считается принятым, пока валидатор не подтвердил —
/// вызывающая сторона (UI/сервис) должна дополнительно требовать подтверждения
/// человеком перед любой записью в БД (human-in-the-loop, см. TZ_ai_local_assistant.md).
/// </summary>
public sealed class GeneratorAgent(IOllamaClient client, IOptions<OllamaOptions> options)
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<AgentResult> RunAsync(
        string systemInstruction,
        string userInput,
        IAgentValidator validator,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", systemInstruction),
            new("user", userInput),
        };

        var output = string.Empty;
        var reason = "not run";

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            output = await client.ChatAsync(messages, cancellationToken);
            var result = validator.Validate(output);
            reason = result.Reason;

            if (result.IsValid)
                return new AgentResult(true, attempt, reason, output);

            messages.Add(new ChatMessage("assistant", output));
            messages.Add(new ChatMessage(
                "user",
                $"Ошибка валидации: {reason}. Верни ИСПРАВЛЕННЫЙ ответ строго по требуемому формату, без лишнего текста."));
        }

        return new AgentResult(false, _options.MaxAttempts, reason, output);
    }
}
