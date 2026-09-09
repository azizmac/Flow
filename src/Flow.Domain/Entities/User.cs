using System.Text.RegularExpressions;

namespace Flow.Domain.Entities;

/// <summary>
/// Пользователь таск-трекера — профиль, который назначают на задачи, упоминают через <c>@Username</c>
/// и показывают в карточке (имя, должность, контакты, внешние ссылки).
/// Учётных данных (пароль, токены, роли) здесь нет намеренно: аутентификация — отдельный контекст,
/// который ссылается на <see cref="Id"/>.
/// Пользователь не удаляется, а деактивируется (<see cref="Deactivate"/>), чтобы не терять историю назначений.
/// </summary>
public sealed partial class User
{
    public const int UsernameMaxLength = 32;
    public const int EmailMaxLength = 254;
    public const int NameMaxLength = 100;
    public const int JobTitleMaxLength = 100;
    public const int BioMaxLength = 1000;
    public const int PhoneNumberMaxLength = 16;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex PhoneNoise();

    private readonly List<UserLink> _links = [];

    public Guid Id { get; private set; }

    /// <summary>Уникальное имя для упоминаний (<c>@ilya</c>) и URL профиля. Всегда в нижнем регистре.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Уникальный e-mail. Всегда в нижнем регистре.</summary>
    public string Email { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    /// <summary>«Имя Фамилия» — для списков назначаемых и карточек. Вычисляется, в БД не хранится.</summary>
    public string FullName => $"{FirstName} {LastName}";

    public string? AvatarUrl { get; private set; }

    /// <summary>Должность, например «Backend-разработчик».</summary>
    public string? JobTitle { get; private set; }

    public string? Bio { get; private set; }

    /// <summary>Телефон в формате E.164 (<c>+79991234567</c>).</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>Можно ли назначать пользователя на новые задачи.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? DeactivatedAt { get; private set; }

    public IReadOnlyCollection<UserLink> Links => _links;

    private User()
    {
        // EF Core
    }

    private User(string username, string email, string firstName, string lastName)
    {
        Id = Guid.NewGuid();
        Username = ValidateUsername(username);
        Email = ValidateEmail(email);
        FirstName = ValidateName(firstName, nameof(firstName));
        LastName = ValidateName(lastName, nameof(lastName));
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Основная точка создания. Уникальность Username/Email между пользователями проверяет Application.</summary>
    public static User Create(string username, string email, string firstName, string lastName)
        => new(username, email, firstName, lastName);

    /// <summary>
    /// Профиль с заранее известным Id — для базового пользователя, чью учётную запись с тем же Id создаёт Flow.Auth
    /// (см. docs/TZ_auth.md). В остальных случаях Id генерирует <see cref="Create"/>.
    /// </summary>
    public static User CreateWithId(Guid id, string username, string email, string firstName, string lastName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("User id must not be empty.", nameof(id));

        return new User(username, email, firstName, lastName) { Id = id };
    }

    public void ChangeUsername(string username) => Username = ValidateUsername(username);

    public void ChangeEmail(string email) => Email = ValidateEmail(email);

    public void ChangeName(string firstName, string lastName)
    {
        FirstName = ValidateName(firstName, nameof(firstName));
        LastName = ValidateName(lastName, nameof(lastName));
    }

    /// <summary><c>null</c> или пустая строка — убрать аватар.</summary>
    public void ChangeAvatar(string? avatarUrl)
        => AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : UserLink.ValidateUrl(avatarUrl, nameof(avatarUrl));

    public void ChangeJobTitle(string? jobTitle)
        => JobTitle = ValidateOptionalText(jobTitle, JobTitleMaxLength, nameof(jobTitle));

    public void ChangeBio(string? bio)
        => Bio = ValidateOptionalText(bio, BioMaxLength, nameof(bio));

    /// <summary>Принимает «+7 (999) 123-45-67», сохраняет «+79991234567». <c>null</c>/пусто — убрать телефон.</summary>
    public void ChangePhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            PhoneNumber = null;
            return;
        }

        var normalized = PhoneNoise().Replace(phoneNumber, string.Empty);
        if (!PhonePattern().IsMatch(normalized))
            throw new ArgumentException("Phone number must be in E.164 format, e.g. +79991234567.", nameof(phoneNumber));

        PhoneNumber = normalized;
    }

    /// <summary>Добавляет ссылку или заменяет URL уже существующей ссылки того же типа.</summary>
    public UserLink SetLink(UserLinkType type, string url)
    {
        var existing = _links.SingleOrDefault(l => l.Type == type);
        if (existing is not null)
        {
            existing.ChangeUrl(url);
            return existing;
        }

        var link = new UserLink(Id, type, url);
        _links.Add(link);
        return link;
    }

    /// <summary>Удаляет ссылку указанного типа. Если её нет — ничего не делает.</summary>
    public void RemoveLink(UserLinkType type) => _links.RemoveAll(l => l.Type == type);

    public void Deactivate()
    {
        if (!IsActive)
            throw new InvalidOperationException($"User {Id} is already deactivated.");

        IsActive = false;
        DeactivatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive)
            throw new InvalidOperationException($"User {Id} is already active.");

        IsActive = true;
        DeactivatedAt = null;
    }

    private static string ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username must not be empty.", nameof(username));

        var normalized = username.Trim().ToLowerInvariant();
        if (normalized.Length > UsernameMaxLength || !UsernamePattern().IsMatch(normalized))
            throw new ArgumentException(
                "Username must be 2-32 characters of [a-z0-9._-] and must start and end with a letter or digit.",
                nameof(username));

        return normalized;
    }

    private static string ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email must not be empty.", nameof(email));

        var normalized = email.Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@');
        if (normalized.Length > EmailMaxLength
            || at < 1
            || at != normalized.LastIndexOf('@')
            || at == normalized.Length - 1
            || normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Email must look like local@domain.", nameof(email));

        return normalized;
    }

    private static string ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be empty.", paramName);

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Name must be at most {NameMaxLength} characters.", paramName);

        return trimmed;
    }

    private static string? ValidateOptionalText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{paramName} must be at most {maxLength} characters.", paramName);

        return trimmed;
    }
}
