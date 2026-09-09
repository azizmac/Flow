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
    /// /connect/authorize не выдаёт код, а ведёт на /account/change-password. Снимается при смене с текущим паролем.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
