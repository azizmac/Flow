using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Search.Commands.ReindexCommand;

/// <summary>
/// Массовая переиндексация (POST /search/reindex). Возвращает число поставленных в очередь источников;
/// сама индексация идёт фоном с Priority = 1 — живые правки всё это время обрабатываются раньше.
/// </summary>
/// <param name="Types">Пусто — все типы.</param>
/// <param name="BoardId">Сузить до одного проекта; для людей игнорируется.</param>
public sealed record ReindexCommand(
    Guid ActorId,
    IReadOnlyList<SearchSourceType>? Types = null,
    Guid? BoardId = null) : IRequest<int>;
