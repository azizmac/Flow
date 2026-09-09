namespace Flow.Auth.Options;

/// <summary>
/// Базовый пользователь, который создаётся при первом запуске (секция "Bootstrap", в Docker — Bootstrap__*).
/// Та же секция с тем же Id читается Flow.Api для профиля Owner, поэтому Id фиксирован и одинаков в обоих сервисах.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; } = true;

    public Guid Id { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public string Username { get; set; } = "admin";

    public string Email { get; set; } = "admin@flow.com";

    /// <summary>Может быть короче политики пароля (например, "admin"): сидер пишет хеш напрямую, минуя валидаторы.</summary>
    public string Password { get; set; } = "admin";
}
