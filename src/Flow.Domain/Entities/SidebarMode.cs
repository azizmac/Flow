namespace Flow.Domain.Entities;

/// <summary>Режим бокового меню по умолчанию (<see cref="UserPreferences.SidebarMode"/>).</summary>
public enum SidebarMode
{
    /// <summary>Решает ширина окна: ниже 1366px меню сворачивается в иконки само.</summary>
    Auto = 0,
    Expanded = 1,
    Collapsed = 2
}
