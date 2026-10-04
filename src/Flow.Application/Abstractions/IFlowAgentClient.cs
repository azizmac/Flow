namespace Flow.Application.Abstractions;

/// <summary>Клиент агента Flow для вопросов по рабочему каталогу исходного кода.</summary>
public interface IFlowAgentClient
{
    Task<OpenCodeAnswer> AskAsync(string workspaceDirectory, string question, CancellationToken cancellationToken);

    Task<OpenCodeAnswer> AskAsync(FlowAgentRequest request, CancellationToken cancellationToken);
}

/// <summary>Контекст вопроса и каталоги, доступные агенту только для чтения.</summary>
/// <param name="ReadOnlyDirectories">Каталоги внутри рабочего контекста; null сохраняет штатные права агента.</param>
public sealed record FlowAgentRequest(
    string WorkspaceDirectory,
    string Question,
    IReadOnlyList<string>? ReadOnlyDirectories = null);

/// <param name="SessionId">Идентификатор сессии OpenCode, в которой был обработан вопрос.</param>
/// <param name="Text">Финальный текстовый ответ агента.</param>
public sealed record OpenCodeAnswer(string SessionId, string Text);
