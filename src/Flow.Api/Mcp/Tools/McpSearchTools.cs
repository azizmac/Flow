using System.ComponentModel;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Users.Queries.UserGetMeQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
using Flow.Shared.Contracts.Search;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Flow.Api.Mcp.Tools;

/// <summary>Поиск по смыслу и справочник людей.</summary>
[McpServerToolType]
public sealed class McpSearchTools(McpContext flow)
{
    private const int MaxLimit = 50;

    [McpServerTool(Name = "whoami", Title = "Кто я", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Профиль пользователя, от имени которого работает этот MCP-сервер.")]
    public Task<McpUser> WhoAmI(CancellationToken cancellationToken) =>
        flow.RunAsync(async () =>
        {
            var me = await flow.Mediator.Send(new UserGetMeQuery(flow.ActorId), cancellationToken)
                ?? throw new McpException("Профиль пользователя не найден.");
            return new McpUser(me.Username, me.FullName, me.Email);
        });

    [McpServerTool(Name = "find_users", Title = "Найти людей", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Активные пользователи по части username, имени или фамилии — чтобы назначить исполнителя.")]
    public Task<IReadOnlyList<McpUser>> FindUsers(
        [Description("Часть username, имени или фамилии.")] string query,
        [Description("Сколько вернуть, до 50.")] int limit = 10,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync<IReadOnlyList<McpUser>>(async () =>
        {
            _ = flow.ActorId; // справочник людей общий, но анонимно его не читают
            var users = await flow.Mediator.Send(
                new UserSearchQuery(query, Math.Clamp(limit, 1, UserSearchQuery.MaxLimit)), cancellationToken);
            return users.Select(u => new McpUser(u.Username, u.FullName, u.Email)).ToList();
        });

    [McpServerTool(Name = "search", Title = "Поиск", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Поиск по смыслу и словам сразу по задачам, комментариям, вложениям, проектам и людям. "
                 + "Подходит для «где обсуждали X», «есть ли уже задача про Y». Для точного отбора задач по полям — search_tasks с FQL.")]
    public Task<McpSearchResult> Search(
        [Description("Запрос обычным языком. Понимает и подсказки: @username, PROJ-142, проект:KEY, мои, просроченные.")] string query,
        [Description("Ограничить проектом: ключ или Id.")] string? project = null,
        [Description("Типы источников через запятую: task, comment, attachment, board, user, development. По умолчанию — все.")] string? types = null,
        [Description("Включать закрытые задачи.")] bool includeClosed = false,
        [Description("Сколько вернуть, до 50.")] int limit = 10,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var boardId = project is null ? (Guid?)null : (await flow.ResolveBoardAsync(project, cancellationToken)).Id;
            var response = await flow.Mediator.Send(new SearchQuery(
                    flow.ActorId, query, ParseTypes(types), boardId, includeClosed, SearchMode.Hybrid,
                    Math.Clamp(limit, 1, MaxLimit), 0),
                cancellationToken) ?? throw new McpException("Поиск на этом сервере выключен. Используй search_tasks.");

            var boards = (await flow.BoardsAsync(cancellationToken)).ToDictionary(b => b.Id, b => b.Key);
            return new McpSearchResult(
                response.Total,
                response.Degraded,
                response.Items.Select(i => new McpSearchHit(
                    i.SourceType,
                    i.Title,
                    i.Snippet.Replace("<mark>", "").Replace("</mark>", ""),
                    i.TaskCode,
                    i.BoardId is { } id && boards.TryGetValue(id, out var key) ? key : null,
                    i.IsClosed,
                    i.UpdatedAt)).ToList());
        });

    private static IReadOnlyList<SearchSourceType>? ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
            return null;

        return types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Equals("project", StringComparison.OrdinalIgnoreCase) ? nameof(SearchSourceType.Board) : t)
            .Select(t => Enum.TryParse<SearchSourceType>(t, ignoreCase: true, out var type)
                ? type
                : throw new McpException($"Неизвестный тип «{t}». Допустимо: {string.Join(", ", Enum.GetNames<SearchSourceType>()).ToLowerInvariant()}."))
            .Distinct()
            .ToList();
    }
}
