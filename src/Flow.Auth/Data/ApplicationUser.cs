using Microsoft.AspNetCore.Identity;

namespace Flow.Auth.Data;

/// <summary>
/// Учётная запись (Identity). Id совпадает с Flow.Domain.User.Id в Flow.Api — так профиль и учётная запись
/// связаны без дополнительных таблиц. Имя, должность, ссылки и роль workspace здесь намеренно отсутствуют:
/// это профиль, которым владеет Flow.Api.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;
