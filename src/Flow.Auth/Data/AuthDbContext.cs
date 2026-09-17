using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Flow.Auth.Data;

/// <summary>
/// Контекст Auth-модуля: таблицы Identity (AspNet*) и OpenIddict (OpenIddict*) живут в схеме "auth"
/// общей базы flow. Таблицы OpenIddict подключаются через options.UseOpenIddict() при регистрации контекста.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("auth");
        base.OnModelCreating(builder);
    }
}
