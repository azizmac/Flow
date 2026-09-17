using MediatR;

namespace Flow.Application.Features.Bootstrap;

/// <summary>
/// Профиль базового пользователя при первом запуске (секция Bootstrap, см. docs/TZ_modular_monolith.md). Учётную запись
/// с тем же Id сеет Auth-модуль в этом же хосте, поэтому IAccountService здесь не участвует.
/// Возвращает true, если профиль создан этим вызовом.
/// Профиль создаётся сразу Owner и Active: это основатель workspace, ему не нужен первый вход для активации.
/// </summary>
public sealed record SeedBootstrapUserCommand(Guid Id, string Username, string Email, string FirstName, string LastName)
    : IRequest<bool>;
