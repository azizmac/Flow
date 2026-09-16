using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchQuery;

/// <summary>
/// Единый поиск по задачам, комментариям, проектам и людям. Response = null, если поиск выключен
/// (Search:Enabled=false) — контроллер отвечает 404.
/// </summary>
/// <remarks>
/// В отличие от остальных Query, поисковый получает ActorId: читать может любая роль, но из выдачи
/// убираются деактивированные люди, а когда появятся приватные проекты, фильтр по правам встанет
/// в тот же WHERE — BoardId у чанка для этого и хранится.
/// </remarks>
/// <param name="Text">Строка запроса; пустая — ArgumentException (400).</param>
/// <param name="Types">Типы источников; null или пусто — все.</param>
/// <param name="IncludeArchived">Включать задачи в финальном статусе; по умолчанию нет.</param>
/// <param name="Rerank">Вторая ступень («Точнее» в интерфейсе): точнее верхушка, дороже на секунду.
/// В подсказках при наборе не используется никогда — там бюджет в десятки миллисекунд.</param>
public sealed record SearchQuery(
    Guid ActorId,
    string Text,
    IReadOnlyList<SearchSourceType>? Types,
    Guid? BoardId,
    bool IncludeArchived,
    SearchMode Mode,
    int Limit,
    int Offset,
    bool Rerank = false) : IRequest<SearchResponse?>;
