using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;

/// <summary>
/// Роль и права actor'а в проектах: BoardId = null — во всех (один запрос на экран со сводным списком),
/// иначе — в одном (пусто, если проекта нет). Клиент прячет кнопки по этим правам; сервер всё равно проверит.
/// </summary>
public sealed record BoardMyAccessQuery(Guid ActorId, Guid? BoardId = null) : IRequest<IReadOnlyList<ProjectAccessResponse>>;
