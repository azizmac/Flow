using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Commands.ReindexCommand;

/// <summary>
/// Ставит в очередь всё подходящее с приоритетом массовой переиндексации. Нужна при смене модели,
/// размерности, инструкции или правил чанкинга и при первом включении поиска на живой базе.
/// ActorId — только Owner. Возвращает число поставленных в очередь источников.
/// </summary>
/// <param name="Types">Типы источников; пусто — все четыре.</param>
/// <param name="BoardId">Ограничить одним проектом; null — вся база.</param>
public sealed record ReindexCommand(Guid ActorId, IReadOnlyList<SearchSourceType>? Types, Guid? BoardId) : IRequest<int>;
