using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Реализуется в Flow.Infrastructure. Уникальность Username/Email страхуется индексами в БД,
/// но проверяется здесь заранее, чтобы контроллер мог ответить 409, а не 500.
/// </summary>
public interface IUserRepository
{
    /// <summary>Пользователь вместе со ссылками, отслеживаемый (для последующего изменения).</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ожидает уже нормализованный username (lower, см. User.Username).</summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Ожидает уже нормализованный email (lower, см. User.Email).</summary>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>
    /// Автодополнение для @упоминаний и выбора исполнителя: ищет по префиксу/вхождению в Username, FirstName, LastName
    /// без учёта регистра, только среди активных, упорядочено по Username.
    /// </summary>
    Task<IReadOnlyList<User>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Пользователи по нормализованным username одним запросом — для резолва @упоминаний в комментариях.
    /// Деактивированных не фильтрует: упоминание ушедшего остаётся ссылкой на профиль. Неизвестные имена пропускаются.
    /// </summary>
    Task<IReadOnlyList<User>> GetByUsernamesAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken);

    /// <summary>Сколько пользователей с ролью — для инварианта «последний Owner нельзя понизить/деактивировать».</summary>
    Task<int> CountByRoleAsync(UserRole role, CancellationToken cancellationToken);

    void Add(User user);
}
