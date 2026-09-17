using Flow.Auth.Data;

namespace Flow.Auth.Security;

/// <summary>Постоянная блокировка (disable) отличается от временной (5 неудачных попыток) горизонтом LockoutEnd.</summary>
public static class LockoutPolicy
{
    public static bool IsDisabled(ApplicationUser user) =>
        user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(100);
}
