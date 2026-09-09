using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserSearchQuery;

/// <summary>Автодополнение для @упоминаний и выбора исполнителя. Пустой запрос — пустой результат.</summary>
public sealed record UserSearchQuery(string Query, int Limit = UserSearchQuery.DefaultLimit)
    : IRequest<IReadOnlyList<UserResponse>>
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;
}
