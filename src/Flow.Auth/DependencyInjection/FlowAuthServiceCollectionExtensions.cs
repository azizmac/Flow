using System.Text.Encodings.Web;
using System.Text.Unicode;
using Flow.Auth.Data;
using Flow.Auth.Options;
using Flow.Auth.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WebEncoders;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flow.Auth.DependencyInjection;

public static class FlowAuthServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует Auth-модуль: Identity + BCrypt, cookie-схему, OpenIddict (server + validation для admin-API),
    /// AuthDbContext на строке подключения "Postgres", сидеры, DataProtection и кодировщик Razor.
    /// Использование в Flow.Auth/Program.cs.
    /// </summary>
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var authSection = configuration.GetSection(AuthOptions.SectionName);
        var auth = authSection.Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(authSection);
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));

        // ---- БД: Identity + OpenIddict в одной базе flow_auth ----
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string \"Postgres\" is not configured.");

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseOpenIddict();
        });

        // ---- Identity: хранилище учётных записей. Роли Identity не используются (роль workspace живёт в Flow.Api). ----
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Длина важнее «спецсимволов»; формат username (^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$) проверяет Flow.Api до вызова.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789._-";

                // Защита от перебора вместо отдельного rate limiter.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager();

        // После AddIdentityCore — иначе Identity вернёт свой PBKDF2-хешер.
        services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher>();
        services.AddHttpContextAccessor();

        // Cookie нужна только между /account/login и /connect/authorize.
        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "flow.auth";
                options.LoginPath = "/account/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

        // ---- OpenIddict ----
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(auth.Issuer, UriKind.Absolute));

                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetUserInfoEndpointUris("connect/userinfo");

                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();

                options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, AuthConstants.ApiScope, AuthConstants.AdminScope);

                // Access token — подписанный JWT без шифрования: Flow.Api валидирует его штатным JwtBearer по JWKS.
                options.DisableAccessTokenEncryption();
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(auth.AccessTokenLifetimeMinutes));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(auth.RefreshTokenLifetimeDays));

                ConfigureKeys(options, auth, environment);

                var aspnet = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();

                // Локальный Docker ходит по http; за TLS-терминатором флаг должен быть выключен.
                if (environment.IsDevelopment() || auth.AllowInsecureHttp)
                    aspnet.DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                // Валидация Bearer-токенов для собственного admin-API: только токены с aud = flow-auth.
                options.UseLocalServer();
                options.UseAspNetCore();
                options.AddAudiences(AuthConstants.AuthResource);
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthConstants.AdminPolicy, policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasScope(AuthConstants.AdminScope)));
        });

        // Cookie, antiforgery и TempData подписываются ключами Data Protection: в контейнере их надо пережить рестарт.
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddScoped<ClientSeeder>();
        services.AddScoped<BootstrapUserSeeder>();
        services.AddHostedService<AuthDatabaseInitializer>();

        // По умолчанию Razor кодирует всё вне Basic Latin в &#x...; — русские тексты страницы входа отдаём как есть.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        return services;
    }

    private static void ConfigureKeys(OpenIddictServerBuilder options, AuthOptions auth, IHostEnvironment environment)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger("Flow.Auth.Certificates");

        if (auth.UseEphemeralKeys)
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            return;
        }

        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
            return;
        }

        // Нет файла — создаётся самоподписанный (Docker: volume auth-certs). Настоящие сертификаты кладутся по тем же путям.
        options.AddSigningCertificate(SelfSignedCertificates.LoadOrCreate(auth.SigningCertificate, "Auth:SigningCertificate", "Flow.Auth signing", logger));
        options.AddEncryptionCertificate(SelfSignedCertificates.LoadOrCreate(auth.EncryptionCertificate, "Auth:EncryptionCertificate", "Flow.Auth encryption", logger));
    }
}
