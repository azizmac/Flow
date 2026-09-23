namespace Flow.Shared.Contracts.Users;

/// <summary>PATCH-семантика: null — не трогать. Экран настроек сохраняет каждое поле сразу и шлёт только его.</summary>
public sealed record UpdateUserPreferencesRequest(
    SidebarMode? SidebarMode = null,
    StartPage? StartPage = null,
    int? TasksPageSize = null);
