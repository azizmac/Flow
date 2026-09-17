namespace Flow.Api.Bootstrap;

/// <summary>
/// Секция "Bootstrap" — общая для модулей одного хоста (в Docker — якорь x-bootstrap). Auth-модуль создаёт учётную
/// запись, ядро — профиль с тем же Id. Password здесь не читается: пароль хранит только Auth-модуль.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; } = true;

    public Guid Id { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public string Username { get; set; } = "admin";

    public string Email { get; set; } = "admin@flow.com";

    public string FirstName { get; set; } = "Admin";

    public string LastName { get; set; } = "Flow";
}
