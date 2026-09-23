using Microsoft.AspNetCore.Identity;

namespace Flow.Auth.Data;

/// <summary>
/// Учётная запись (Identity). Id совпадает с Flow.Domain.User.Id в Flow.Api — так профиль и учётная запись
/// связаны без дополнительных таблиц. Имя, должность, ссылки и роль workspace здесь намеренно отсутствуют:
/// это профиль, которым владеет Flow.Api.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Пароль задан не самим человеком (bootstrap, начальный от админа, сброс Owner'ом): до его смены
    /// /connect/authorize не выдаёт код, а ведёт на /account/change-password. Снимается при смене с текущим паролем
    /// или явным «Оставить как есть» на той же странице (тогда ставится <see cref="PasswordRiskAcceptedAt"/>).
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Когда человек отказался менять заданный за него пароль и принял риск на себя. Нужен не только для аудита:
    /// без него BootstrapUserSeeder на каждом старте снова ставил бы MustChangePassword базовому пользователю
    /// со стандартным паролем. Любая смена или сброс пароля его обнуляет — согласие относилось к прежнему паролю.
    /// </summary>
    public DateTimeOffset? PasswordRiskAcceptedAt { get; set; }
}
