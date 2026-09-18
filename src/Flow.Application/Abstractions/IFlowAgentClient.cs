namespace Flow.Application.Abstractions;

/// <summary>Клиент агента Flow для вопросов по рабочему каталогу исходного кода.</summary>
public interface IFlowAgentClient
{
    Task<OpenCodeAnswer> AskAsync(string workspaceDirectory, string question, CancellationToken cancellationToken);
}

/// <param name="SessionId">Идентификатор сессии OpenCode, в которой был обработан вопрос.</param>
/// <param name="Text">Финальный текстовый ответ агента.</param>
public sealed record OpenCodeAnswer(string SessionId, string Text);
