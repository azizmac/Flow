using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardListQuery;

/// <summary>Проекты, которые actor видит: приватные — только участникам и глобальным Admin/Owner.</summary>
public sealed record BoardListQuery(Guid ActorId) : IRequest<IReadOnlyList<BoardResponse>>;
