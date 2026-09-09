namespace Flow.Domain.Entities;

/// <summary>Статус отделён от роли. Заменяет прежние IsActive/DeactivatedAt; IsActive остаётся вычисляемым.</summary>
public enum UserStatus
{
    /// <summary>Создан, ещё не входил. Назначать на задачи можно.</summary>
    Invited = 0,

    Active = 1,

    /// <summary>Ушёл: вход заблокирован, назначать нельзя, история назначений сохраняется.</summary>
    Deactivated = 2
}
