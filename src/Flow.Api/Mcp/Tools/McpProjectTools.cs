using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Flow.Api.Mcp.Tools;

/// <summary>Проекты: какие видны и как устроены (статусы, типы, поля) — без этого модель не назовёт статус.</summary>
[McpServerToolType]
public sealed class McpProjectTools(McpContext flow)
{
    [McpServerTool(Name = "list_projects", Title = "Проекты", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Проекты, видимые текущему пользователю: ключ (префикс кодов задач), имя и число задач.")]
    public Task<IReadOnlyList<McpProjectSummary>> ListProjects(CancellationToken cancellationToken) =>
        flow.RunAsync<IReadOnlyList<McpProjectSummary>>(async () =>
            (await flow.BoardsAsync(cancellationToken))
                .Select(b => new McpProjectSummary(b.Id, b.Key, b.Name, b.TaskCount))
                .ToList());

    [McpServerTool(Name = "get_project", Title = "Проект", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Устройство проекта: статусы (в порядке workflow), типы задач и пользовательские поля. "
                 + "Вызывай перед create_task / update_task, чтобы знать допустимые имена статусов и типов.")]
    public Task<McpProject> GetProject(
        [Description("Ключ проекта (PROJ), его имя или Id.")] string project,
        CancellationToken cancellationToken) =>
        flow.RunAsync(async () =>
        {
            var board = await flow.ResolveBoardAsync(project, cancellationToken);
            var fields = board.CustomFields?
                .Where(f => !f.IsArchived)
                .Select(f => new McpCustomField(f.Key, f.Name, f.Type, f.IsRequired,
                    f.Options.Count == 0 ? null : f.Options.Select(o => o.Label).ToList()))
                .ToList();

            return new McpProject(
                board.Id,
                board.Key,
                board.Name,
                board.TaskCount,
                board.WorkflowMode,
                board.Statuses.Select(s => new McpStatus(s.Name, s.Type, s.IsInitial, s.IsFinal)).ToList(),
                board.TaskTypes.Where(t => !t.IsArchived).Select(t => new McpTaskType(t.Name, t.Kind, t.IsDefault)).ToList(),
                fields is { Count: > 0 } ? fields : null);
        });
}
