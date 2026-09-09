using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserListQuery;

/// <summary>По умолчанию только активные — деактивированные не должны попадать в списки выбора исполнителя.</summary>
public sealed record UserListQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<UserResponse>>;
