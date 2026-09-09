namespace Flow.Domain.Entities;

/// <summary>
/// Тип внешней ссылки в профиле пользователя. У пользователя может быть не более одной ссылки каждого типа
/// (см. <see cref="User.SetLink"/>). Новые соцсети добавляются сюда без изменения схемы таблицы.
/// </summary>
public enum UserLinkType
{
    GitHub = 0,
    GitLab = 1,
    Telegram = 2,
    LinkedIn = 3,
    Website = 4,
    Other = 5
}
