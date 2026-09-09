using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetQuery;

public sealed record UserGetQuery(Guid UserId) : IRequest<UserResponse?>;
