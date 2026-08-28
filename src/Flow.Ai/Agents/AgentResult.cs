namespace Flow.Ai.Agents;

public sealed record AgentResult(bool Success, int Attempts, string Reason, string RawOutput);
