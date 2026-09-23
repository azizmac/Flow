namespace Flow.Shared.Contracts.Users;

/// <summary>
/// Личные настройки интерфейса текущего пользователя — GET/PATCH /users/me/preferences.
/// Чужие настройки не читаются никем, поэтому в <see cref="UserResponse"/> их нет.
/// </summary>
/// <param name="AllowedTasksPageSizes">Допустимые значения <paramref name="TasksPageSize"/> — чтобы клиент не держал копию списка.</param>
public sealed record UserPreferencesResponse(
    SidebarMode SidebarMode,
    StartPage StartPage,
    int TasksPageSize,
    IReadOnlyList<int> AllowedTasksPageSizes);
