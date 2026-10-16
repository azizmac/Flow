using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using ModelContextProtocol;

namespace Flow.Api.Mcp;

/// <summary>
/// Общее для инструментов MCP: actor запроса, разбор того, как модель называет вещи (ключ проекта, код задачи,
/// username, имя статуса), в Id — и обратно, при сборке ответа. Scoped — один вызов инструмента: кэш проектов
/// и людей живёт ровно столько, сколько собирается один ответ, и снятый доступ действует со следующего вызова.
/// </summary>
public sealed class McpContext(IMediator mediator, IActorAccessor actor, IHttpContextAccessor http)
{
    public const string Me = "me";
    public const string Nobody = "none";

    private readonly Dictionary<Guid, BoardResponse?> _boards = [];
    private readonly Dictionary<Guid, string?> _usernames = [];
    private readonly Dictionary<Guid, string?> _taskCodes = [];
    private IReadOnlyList<BoardResponse>? _boardList;

    public IMediator Mediator => mediator;

    public Guid ActorId => actor.Require();

    /// <summary>
    /// Ошибки Application в текст для модели. Сообщение McpException уходит клиенту как результат инструмента
    /// с isError — модель видит причину («нет прав», «статус не найден») и может поправиться; любое другое
    /// исключение SDK заменяет безликим «An error occurred», и чинить было бы нечего.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (FqlException ex)
        {
            throw new McpException($"Ошибка FQL (позиция {ex.Position}): {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedActorException or ForbiddenException or ProjectNotFoundException
                                       or ArgumentException or InvalidOperationException)
        {
            throw new McpException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<BoardResponse>> BoardsAsync(CancellationToken cancellationToken) =>
        _boardList ??= await mediator.Send(new BoardListQuery(ActorId), cancellationToken);

    /// <summary>Проект по ключу, Id или точному имени. Скрытый и несуществующий неотличимы — как 404 в API.</summary>
    public async Task<BoardResponse> ResolveBoardAsync(string project, CancellationToken cancellationToken)
    {
        var value = project.Trim();
        if (Guid.TryParse(value, out var id))
            return await BoardAsync(id, cancellationToken) ?? throw NotFound($"Проект «{value}» не найден.");

        var boards = await BoardsAsync(cancellationToken);
        var match = boards.FirstOrDefault(b => string.Equals(b.Key, value, StringComparison.OrdinalIgnoreCase))
            ?? boards.FirstOrDefault(b => string.Equals(b.Name, value, StringComparison.OrdinalIgnoreCase))
            ?? throw NotFound($"Проект «{value}» не найден. Доступные: {string.Join(", ", boards.Select(b => b.Key))}.");

        // В списке проектов статусы и типы те же, но полный ответ дешевле перечитать, чем гадать, что в нём урезано.
        return await BoardAsync(match.Id, cancellationToken) ?? throw NotFound($"Проект «{value}» не найден.");
    }

    public async Task<BoardResponse?> BoardAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_boards.TryGetValue(id, out var board))
            _boards[id] = board = await mediator.Send(new BoardGetQuery(ActorId, id), cancellationToken);
        return board;
    }

    /// <summary>Задача по коду (живому или прежнему — после переноса) или Id.</summary>
    public async Task<TaskResponse> ResolveTaskAsync(string task, CancellationToken cancellationToken)
    {
        var value = task.Trim();
        var found = Guid.TryParse(value, out var id)
            ? await mediator.Send(new TaskGetQuery(ActorId, id), cancellationToken)
            : await mediator.Send(new TaskGetByCodeQuery(ActorId, value.ToUpperInvariant()), cancellationToken);
        return found ?? throw NotFound($"Задача «{value}» не найдена.");
    }

    /// <summary>me / none / username (можно с @) / Id. none → null: «снять исполнителя».</summary>
    public async Task<Guid?> ResolveUserAsync(string user, CancellationToken cancellationToken)
    {
        var value = user.Trim().TrimStart('@');
        if (string.Equals(value, Nobody, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(value, Me, StringComparison.OrdinalIgnoreCase))
            return ActorId;
        if (Guid.TryParse(value, out var id))
            return id;

        var found = await mediator.Send(new UserGetByUsernameQuery(value.ToLowerInvariant()), cancellationToken);
        return found?.Id ?? throw NotFound($"Пользователь «{value}» не найден. Поиск людей — find_users.");
    }

    public static StatusResponse ResolveStatus(BoardResponse board, string status)
    {
        var value = status.Trim();
        return board.Statuses.FirstOrDefault(s => Guid.TryParse(value, out var id) && s.Id == id
                                                  || string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException(
                $"В проекте {board.Key} нет статуса «{value}». Статусы: {string.Join(", ", board.Statuses.Select(s => s.Name))}.");
    }

    /// <summary>Тип по имени, Id или виду (epic/story/task/bug/subtask — первый неархивный этого вида).</summary>
    public static TaskTypeResponse ResolveType(BoardResponse board, string type)
    {
        var value = type.Trim();
        var active = board.TaskTypes.Where(t => !t.IsArchived).ToList();
        return active.FirstOrDefault(t => Guid.TryParse(value, out var id) && t.Id == id
                                          || string.Equals(t.Name, value, StringComparison.OrdinalIgnoreCase))
            ?? (Enum.TryParse<TaskTypeKind>(value, ignoreCase: true, out var kind) ? active.FirstOrDefault(t => t.Kind == kind) : null)
            ?? throw new McpException(
                $"В проекте {board.Key} нет типа «{value}». Типы: {string.Join(", ", active.Select(t => t.Name))}.");
    }

    public static TaskPriority ParsePriority(string priority) =>
        Enum.TryParse<TaskPriority>(priority.Trim(), ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new McpException($"Неизвестный приоритет «{priority}». Допустимо: {string.Join(", ", Enum.GetNames<TaskPriority>())}.");

    public async Task<string?> UsernameAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } userId)
            return null;
        if (!_usernames.TryGetValue(userId, out var name))
            _usernames[userId] = name = (await mediator.Send(new UserGetQuery(userId), cancellationToken))?.Username;
        return name;
    }

    /// <summary>Код задачи по Id — для родителя. Невидимая задача (другой приватный проект) — null.</summary>
    public async Task<string?> TaskCodeAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } taskId)
            return null;
        if (!_taskCodes.TryGetValue(taskId, out var code))
            _taskCodes[taskId] = code = (await mediator.Send(new TaskGetQuery(ActorId, taskId), cancellationToken))?.Code;
        return code;
    }

    /// <summary>Ссылка на карточку задачи в интерфейсе — от адреса, по которому пришёл сам клиент MCP.</summary>
    public string TaskUrl(string code)
    {
        var request = http.HttpContext?.Request;
        return request is null ? $"/tasks/{code}" : $"{request.Scheme}://{request.Host}{request.PathBase}/tasks/{code}";
    }

    public async Task<McpTask> ToViewAsync(TaskResponse task, bool withDescription, CancellationToken cancellationToken)
    {
        var board = await BoardAsync(task.BoardId, cancellationToken);
        var status = board?.Statuses.FirstOrDefault(s => s.Id == task.StatusId);
        var type = board?.TaskTypes.FirstOrDefault(t => t.Id == task.TypeId);

        return new McpTask(
            task.Id,
            task.Code,
            task.Title,
            board?.Key,
            status?.Name,
            status?.Type,
            status?.IsFinal ?? false,
            type?.Name,
            task.Priority,
            await UsernameAsync(task.AssigneeId, cancellationToken),
            await UsernameAsync(task.CreatedById, cancellationToken),
            withDescription ? task.Description ?? "" : null,
            task.StartDate,
            task.DueDate,
            task.StoryPoints,
            task.EstimateMinutes,
            await TaskCodeAsync(task.ParentId, cancellationToken),
            task.ChildCount == 0 ? null : $"{task.ChildDoneCount}/{task.ChildCount}",
            task.ChecklistTotal == 0 ? null : $"{task.ChecklistDone}/{task.ChecklistTotal}",
            task.CommentCount,
            task.BlockedByCount == 0 ? null : task.BlockedByCount,
            task.CreatedAt,
            task.UpdatedAt,
            TaskUrl(task.Code));
    }

    private static McpException NotFound(string message) => new(message);
}
