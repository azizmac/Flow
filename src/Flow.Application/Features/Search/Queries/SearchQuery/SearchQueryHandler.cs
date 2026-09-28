using System.Diagnostics;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchQuery;

internal sealed class SearchQueryHandler(
    ISearchQueryRepository index,
    IQueryEmbeddingCache queryEmbeddings,
    IVisionEmbeddingGenerator vision,
    IReranker reranker,
    IBoardRepository boards,
    ITaskItemRepository tasks,
    IUserRepository users,
    SearchOptions options,
    ActorResolver actors,
    IProjectAccess projectAccess)
    : IRequestHandler<SearchQuery, SearchResponse?>
{
    /// <summary>Что ищется, когда тип не выбран. Вложения входят сюда наравне с остальным: файл,
    /// который виден только при явном фильтре «Файлы», для человека всё равно что не найден.</summary>
    private static readonly SearchSourceType[] AllTypes =
        [SearchSourceType.Task, SearchSourceType.Comment, SearchSourceType.Board, SearchSourceType.User, SearchSourceType.Attachment, SearchSourceType.Development];

    /// <summary>Сколько текста описания уходит в подсказку прямого попадания по коду задачи.</summary>
    private const int DirectSnippetLength = 160;

    public async Task<SearchResponse?> Handle(SearchQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        if (!options.Enabled)
            return null;

        var intent = QueryIntentParser.Parse(request.Text);
        if (intent.Text.Length == 0 && !intent.HasFilters)
            throw new ArgumentException("Запрос поиска не может быть пустым.", nameof(request.Text));

        var started = Stopwatch.GetTimestamp();
        // Видимые проекты — одним запросом на весь поиск: ими фильтруются и выдача индекса (BoardId чанка),
        // и разбор строки (проект:KEY, статусы, код задачи) — скрытый проект не должен угадываться даже по ключу.
        var visible = await projectAccess.VisibleBoardIdsAsync(actor, cancellationToken);
        var resolved = await ResolveAsync(intent, actor, request, visible, cancellationToken);

        // Код задачи в строке — это не поиск, а прямое попадание: задача идёт первой в выдаче,
        // а клиент может открыть её сразу.
        var direct = resolved.Task is { } found ? ToItem(found, resolved.TaskIsClosed) : null;

        // «PROJ-142» и больше ничего: искать нечего, отдаём только саму задачу.
        if (resolved.Text.Length == 0 && !HasSearchableFilters(resolved))
            return Respond(direct is null ? [] : [direct], direct is null ? 0 : 1, false, SearchMode.Filters, intent, resolved, direct, started);

        var query = options.Query;
        float[]? embedding = null;
        var degraded = false;

        // Визуальная половина — тот же запрос в пространстве картинок. Запускается до текстовой и
        // ожидается после: это другая модель на своём сервисе, ждать их по очереди незачем.
        // Без вложений в типах она бессмысленна — визуальные чанки есть только у них.
        var visionTask = vision.IsConfigured
                         && request.Mode != SearchMode.Text
                         && resolved.Text.Length > 0
                         && resolved.Types.Contains(SearchSourceType.Attachment)
            ? vision.EmbedQueryAsync(resolved.Text, cancellationToken)
            : null;

        // Векторная половина нужна во всех режимах, кроме Text, и только когда есть что эмбеддить.
        // Выключенные эмбеддинги — не деградация, а настройка: выдача честно остаётся текстовой,
        // и клиент не показывает «модель недоступна» там, где её просто не просили.
        if (request.Mode != SearchMode.Text && resolved.Text.Length > 0 && options.Embeddings.Enabled)
        {
            try
            {
                embedding = await queryEmbeddings.GetAsync(resolved.Text, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Логирует сторона, которая ходила в модель (см. MemoryCachedQueryEmbeddings):
                // Application про логгер не знает и знать не обязан.
                degraded = true;
            }
        }

        var visionEmbedding = await AwaitVisionAsync(visionTask);

        var useText = resolved.Text.Length > 0 && (request.Mode != SearchMode.Semantic || embedding is null);

        var mode = (embedding, useText) switch
        {
            (not null, true) => SearchMode.Hybrid,
            (not null, false) => SearchMode.Semantic,
            (null, true) => SearchMode.Text,
            _ => SearchMode.Filters
        };

        var limit = Math.Clamp(request.Limit, 1, Math.Max(1, query.MaxLimit));
        var offset = Math.Max(0, request.Offset);

        // Вторая ступень работает только по первой странице: переупорядочивать окно, которое человек
        // уже пролистал, незачем, а тянуть ради этого лишние кандидаты — значит платить за каждую пару.
        var rerankWindow = request.Rerank && options.RerankEnabled && reranker.IsConfigured && offset == 0 && resolved.Text.Length > 0;
        var candidates = rerankWindow ? Math.Max(options.Rerank.TopN, limit) : limit;

        var criteria = new SearchCriteria(
            Query: resolved.Text,
            QueryEmbedding: embedding,
            UseText: useText,
            Types: resolved.Types,
            BoardId: resolved.BoardId,
            IncludeArchived: request.IncludeArchived,
            VectorTopN: query.VectorTopN,
            TextTopN: query.TextTopN,
            RrfK: query.RrfK,
            Limit: candidates,
            Offset: offset,
            AssigneeId: resolved.AssigneeId,
            StatusIds: resolved.StatusIds,
            OverdueOnly: intent.Overdue,
            UpdatedSince: resolved.UpdatedSince,
            VisionQueryEmbedding: visionEmbedding,
            VisibleBoardIds: visible);

        var page = await index.SearchAsync(criteria, cancellationToken);

        var hits = page.Items;
        var reranked = false;
        if (rerankWindow)
        {
            (hits, reranked) = await RerankAsync(resolved.Text, hits, cancellationToken);
            // Окно кандидатов шире страницы, и обрезать его нужно в любом случае — в том числе когда
            // модель промолчала: человек просил страницу, а не два десятка результатов.
            hits = hits.Take(limit).ToArray();
        }

        var items = hits.Select(ToItem).ToList();
        var total = page.Total;

        if (direct is not null)
        {
            // Та же задача могла найтись и текстом — оставляем её один раз и первой.
            var duplicates = items.RemoveAll(item => item.SourceType == SearchSourceType.Task && item.SourceId == direct.SourceId);
            items.Insert(0, direct);
            total += duplicates > 0 ? 0 : 1;
        }

        return Respond(items, total, degraded, mode, intent, resolved, direct, started, reranked);
    }

    /// <summary>
    /// Вектор запроса в визуальном пространстве. Недоступная модель картинок не ломает поиск:
    /// выпадает только визуальная половина, текстовая отдаёт выдачу как обычно.
    /// </summary>
    private static async Task<float[]?> AwaitVisionAsync(Task<float[]>? task)
    {
        if (task is null)
            return null;

        try
        {
            return await task;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Логирует сторона, которая ходила в модель (HttpVisionEmbeddingGenerator).
            return null;
        }
    }

    /// <summary>
    /// Переупорядочивает отобранное первой ступенью. Недоступная модель — не ошибка запроса: выдача
    /// уже есть, она просто остаётся гибридной, и клиент видит это по Reranked = false.
    /// </summary>
    /// <remarks>
    /// Находки визуальной половины (<see cref="SearchHit.IsVisual"/>) модели не показываются и остаются
    /// на своих местах: реранкер текстовый, а у картинки, найденной по кадру, в чанке только имя файла.
    /// Оценил бы он «IMG_1234.jpg» против запроса «красный квадрат» — и утопил бы ровно то, что нашла
    /// визуальная модель. Текстовые находки пересортировываются между собой на оставшихся местах.
    /// </remarks>
    private async Task<(IReadOnlyList<SearchHit> Hits, bool Reranked)> RerankAsync(
        string query,
        IReadOnlyList<SearchHit> hits,
        CancellationToken cancellationToken)
    {
        var slots = hits.Select((hit, position) => (hit, position))
            .Where(pair => !pair.hit.IsVisual)
            .Select(pair => pair.position)
            .ToArray();

        if (slots.Length <= 1)
            return (hits, false);

        var textual = slots.Select(position => hits[position]).ToArray();

        try
        {
            var scores = await reranker.RankAsync(query, textual.Select(Document).ToArray(), cancellationToken);
            if (scores.Count == 0)
                return (hits, false);

            var ordered = scores.OrderByDescending(score => score.Score).Select(score => textual[score.Index]).ToList();

            // Документы, которых модель не оценила, уходят в хвост в исходном порядке: выбросить их
            // нельзя — первая ступень их уже нашла, а человек ждёт полную страницу.
            var scored = scores.Select(score => score.Index).ToHashSet();
            ordered.AddRange(textual.Where((_, index) => !scored.Contains(index)));

            var result = hits.ToArray();
            for (var i = 0; i < slots.Length; i++)
                result[slots[i]] = ordered[i];

            return (result, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (hits, false);
        }
    }

    /// <summary>
    /// Что видит cross-encoder: заголовок и лучший чанк без подсветки, обрезанные по лимиту —
    /// он платит за каждый токен пары, а решают обычно первые строки.
    /// </summary>
    private string Document(SearchHit hit)
    {
        var text = string.IsNullOrWhiteSpace(hit.Content) ? hit.Title : $"{hit.Title}\n{hit.Content}";
        var max = Math.Max(200, options.Rerank.MaxDocumentChars);
        return text.Length <= max ? text : text[..max];
    }

    private static SearchResponse Respond(
        IReadOnlyList<SearchResultItem> items,
        int total,
        bool degraded,
        SearchMode mode,
        SearchIntent intent,
        ResolvedIntent resolved,
        SearchResultItem? direct,
        long started,
        bool reranked = false) =>
        new(items,
            total,
            degraded,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            mode,
            new SearchIntentResponse(resolved.Text, Labels(intent, resolved), direct?.SourceId),
            reranked);

    /// <summary>
    /// Достраивает разобранную строку до фильтров, которые понимает индекс: @username → id,
    /// проект:KEY → id, статус:… → набор id (название статуса своё у каждого проекта).
    /// Нераспознанное возвращается в текст запроса — опечатка в фильтре не должна обнулять выдачу.
    /// </summary>
    private async Task<ResolvedIntent> ResolveAsync(
        SearchIntent intent, User actor, SearchQuery request, IReadOnlyCollection<Guid>? visible, CancellationToken cancellationToken)
    {
        var text = intent.Text;
        Guid? assigneeId = intent.Mine ? actor.Id : null;
        var boardId = request.BoardId;
        IReadOnlyCollection<Guid> statusIds = [];
        TaskItem? task = null;

        // Проекты со статусами нужны трём разным веткам ниже, а запрос один и тот же: их единицы,
        // но дёргать его трижды незачем — и незачем вовсе, когда ни одна ветка не сработала.
        IReadOnlyList<Board>? allBoards = null;
        async Task<IReadOnlyList<Board>> BoardsAsync() => allBoards ??=
            (await boards.GetAllAsync(cancellationToken)).Where(b => visible is null || visible.Contains(b.Id)).ToList();

        if (intent.AssigneeUsername is { } username)
        {
            var user = await users.GetByUsernameAsync(username, cancellationToken);
            if (user is null)
                text = Append(text, "@" + username);
            else
                assigneeId = user.Id;
        }

        if (intent.TaskCode is { } code)
        {
            // Код задачи скрытого проекта — как несуществующий: остаётся текстом запроса.
            task = await tasks.GetByCodeAsync(code, cancellationToken);
            if (task is not null && visible is not null && !visible.Contains(task.BoardId))
                task = null;
            if (task is null)
                text = Append(text, code);
        }

        if (intent.BoardKey is not null || intent.StatusName is not null)
        {
            var all = await BoardsAsync();

            if (intent.BoardKey is { } key)
            {
                var board = all.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(b => b.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase));

                if (board is null)
                    text = Append(text, key);
                else
                    boardId = board.Id;
            }

            if (intent.StatusName is { } statusName)
            {
                var matched = all
                    .Where(b => boardId is null || b.Id == boardId)
                    .SelectMany(b => b.Statuses)
                    .Where(status => string.Equals(status.Name, statusName, StringComparison.OrdinalIgnoreCase))
                    .Select(status => status.Id)
                    .ToArray();

                if (matched.Length == 0)
                    text = Append(text, statusName);
                else
                    statusIds = matched;
            }
        }

        // Прямое попадание по коду минует индекс, а значит и флаг закрытости из чанка: финальность
        // статуса читается здесь, иначе закрытая задача приезжала бы в выдачу без пометки «архив».
        var taskIsClosed = task is not null && await IsFinalAsync(task, BoardsAsync);

        // Исполнитель, статус и срок есть только у задач: проект «просроченным» не бывает.
        IReadOnlyCollection<SearchSourceType> types =
            intent.Overdue || assigneeId is not null || statusIds.Count > 0
                ? [SearchSourceType.Task]
                : request.Types is { Count: > 0 } requested ? requested.Distinct().ToArray() : AllTypes;

        return new ResolvedIntent(
            text.Trim(),
            types,
            boardId,
            assigneeId,
            statusIds,
            intent.Period is { } period ? DateTime.UtcNow - period : null,
            task,
            taskIsClosed);
    }

    private static async Task<bool> IsFinalAsync(TaskItem task, Func<Task<IReadOnlyList<Board>>> boards) =>
        (await boards())
            .FirstOrDefault(board => board.Id == task.BoardId)?
            .Statuses.FirstOrDefault(status => status.Id == task.StatusId)?
            .IsFinal ?? false;

    /// <summary>Есть ли что отбирать, кроме прямого попадания по коду задачи.</summary>
    private static bool HasSearchableFilters(ResolvedIntent resolved) =>
        resolved.AssigneeId is not null
        || resolved.StatusIds.Count > 0
        || resolved.BoardId is not null
        || resolved.UpdatedSince is not null
        || resolved.Types.Count == 1;

    private static List<string> Labels(SearchIntent intent, ResolvedIntent resolved)
    {
        var labels = new List<string>();

        if (resolved.Task is { } task)
            labels.Add(task.Code.Value);

        if (intent.Mine)
            labels.Add("мои");
        else if (intent.AssigneeUsername is { } username && resolved.AssigneeId is not null)
            labels.Add("@" + username);

        if (intent.Overdue)
            labels.Add("просроченные");

        if (intent.BoardKey is { } key && resolved.BoardId is not null)
            labels.Add("проект " + key.ToUpperInvariant());

        if (intent.StatusName is { } status && resolved.StatusIds.Count > 0)
            labels.Add("статус: " + status);

        if (intent.Period is { } period)
            labels.Add(period.TotalDays switch
            {
                <= 1 => "за день",
                <= 7 => "за неделю",
                <= 30 => "за месяц",
                _ => "за год"
            });

        return labels;
    }

    private static string Append(string text, string value) => text.Length == 0 ? value : $"{text} {value}";

    private static SearchResultItem ToItem(SearchHit hit) =>
        new(hit.SourceType, hit.SourceId, hit.BoardId, hit.Title, hit.Snippet, hit.Score, hit.TaskCode, hit.UpdatedAt,
            hit.ParentId, hit.IsClosed);

    /// <summary>Прямое попадание собирается из самой задачи: в индексе её может ещё не быть.</summary>
    private static SearchResultItem ToItem(TaskItem task, bool isClosed) =>
        new(SearchSourceType.Task,
            task.Id,
            task.BoardId,
            task.Title,
            task.Description is { Length: > 0 } description
                ? description[..Math.Min(DirectSnippetLength, description.Length)]
                : string.Empty,
            Score: 1,
            task.Code.Value,
            task.CreatedAt,
            ParentId: null,
            IsClosed: isClosed);

    private sealed record ResolvedIntent(
        string Text,
        IReadOnlyCollection<SearchSourceType> Types,
        Guid? BoardId,
        Guid? AssigneeId,
        IReadOnlyCollection<Guid> StatusIds,
        DateTime? UpdatedSince,
        TaskItem? Task,
        bool TaskIsClosed = false);
}
