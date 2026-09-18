namespace Flow.Shared.Contracts.Agents;

public sealed record AgentTestRequest(string Question, string WorkspaceDirectory = "/workspaces");
