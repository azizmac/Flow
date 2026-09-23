using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserUpdatePreferencesCommand;

/// <summary>
/// Меняет личные настройки самого actor — у команды нет UserId намеренно: править чужие настройки
/// не может никто, даже Owner, поэтому и строки в матрице прав у неё нет. PATCH-семантика: null — не трогать.
/// Недопустимое значение — ArgumentException (400).
/// </summary>
public sealed record UserUpdatePreferencesCommand(
    Guid ActorId,
    SidebarMode? SidebarMode,
    StartPage? StartPage,
    int? TasksPageSize) : IRequest<UserPreferencesResponse>;
