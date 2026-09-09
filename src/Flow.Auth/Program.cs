using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Flow.Auth.Data;
using Flow.Auth.Options;
using Flow.Auth.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

var authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
var auth = authSection.Get<AuthOptions>() ?? new AuthOptions();
builder.Services.Configure<AuthOptions>(authSection);
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));

// ---- БД: Identity + OpenIddict в одной базе flow_auth ----
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string \"Postgres\" is not configured.");

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseOpenIddict();
});

// ---- Identity: хранилище учётных записей. Роли Identity не используются (роль workspace живёт в Flow.Api). ----
builder.Services
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
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher>();
builder.Services.AddHttpContextAccessor();

// Cookie нужна только между /account/login и /connect/authorize.
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "flow.auth";
        options.LoginPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// ---- OpenIddict ----
builder.Services.AddOpenIddict()
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

        ConfigureKeys(options, auth, builder.Environment);

        var aspnet = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();

        // Локальный Docker ходит по http; за TLS-терминатором флаг должен быть выключен.
        if (builder.Environment.IsDevelopment() || auth.AllowInsecureHttp)
            aspnet.DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        // Валидация Bearer-токенов для собственного admin-API: только токены с aud = flow-auth.
        options.UseLocalServer();
        options.UseAspNetCore();
        options.AddAudiences(AuthConstants.AuthResource);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthConstants.AdminPolicy, policy => policy
        .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.HasScope(AuthConstants.AdminScope)));
});

// Cookie, antiforgery и TempData подписываются ключами Data Protection: в контейнере их надо пережить рестарт.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddScoped<ClientSeeder>();
builder.Services.AddScoped<BootstrapUserSeeder>();
builder.Services.AddHostedService<AuthDatabaseInitializer>();

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// По умолчанию Razor кодирует всё вне Basic Latin в &#x...; — русские тексты страницы входа отдаём как есть.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// Flow.Client ходит на discovery и /connect/token из браузера — нужен CORS на его origin'ы.
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseStaticFiles();
app.UseCors(clientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Flow.Auth");
app.MapRazorPages();
app.MapControllers();

app.Run();

static void ConfigureKeys(OpenIddictServerBuilder options, AuthOptions auth, IHostEnvironment environment)
{
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

    options.AddSigningCertificate(LoadCertificate(auth.SigningCertificate, "Auth:SigningCertificate"));
    options.AddEncryptionCertificate(LoadCertificate(auth.EncryptionCertificate, "Auth:EncryptionCertificate"));
}

static X509Certificate2 LoadCertificate(AuthOptions.CertificateOptions certificate, string section)
{
    if (string.IsNullOrWhiteSpace(certificate.Path))
        throw new InvalidOperationException($"{section}:Path is required outside Development (PFX with the private key).");

    return X509CertificateLoader.LoadPkcs12FromFile(certificate.Path, certificate.Password, X509KeyStorageFlags.EphemeralKeySet);
}

/// <summary>Для WebApplicationFactory в Flow.Auth.Tests.</summary>
public partial class Program;
