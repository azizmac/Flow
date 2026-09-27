using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMembersQuery;

/// <summary>Роль по умолчанию и участники проекта; null — проекта нет. Читать может любой, кто видит проект.</summary>
public sealed record BoardMembersQuery(Guid ActorId, Guid BoardId) : IRequest<BoardMembersResponse?>;
