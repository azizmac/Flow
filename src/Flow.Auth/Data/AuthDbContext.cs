using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Flow.Auth.Data;

/// <summary>
/// База сервиса аутентификации (flow_auth): таблицы Identity (AspNet*) и OpenIddict (OpenIddict*).
/// Таблицы OpenIddict подключаются через options.UseOpenIddict() при регистрации контекста.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options);
