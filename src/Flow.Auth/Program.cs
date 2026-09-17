using System.Text.Encodings.Web;
using System.Text.Unicode;
using Flow.Auth.Data;
using Flow.Auth.Options;
using Flow.Auth.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

        ConfigureKeys(options, auth, builder.Environment, builder.Logging);

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

// ---- Пробы Kubernetes: /health/live и /health/ready (docs/TZ_cicd_k8s.md §9 п.2) ----
// Две пробы, а не одна, потому что вопросы разные. live — «процесс жив», без единого обращения к БД:
// иначе упавший Postgres перезапускал бы по кругу заведомо исправный контейнер и чинить было бы нечего.
// ready — «можно слать трафик»; его набор проверок помечен тегом ready, живой набор пуст.
//
// ТРЕБОВАНИЯ К МАНИФЕСТУ — не пожелания, без них выкатка ломается на ровном месте:
//
// 1) startupProbe ОБЯЗАТЕЛЕН. GenericWebHostService (Kestrel) регистрируется внутри builder.Build() ниже,
//    то есть ПОСЛЕ AddHostedService<AuthDatabaseInitializer>() выше, а хост стартует hosted services строго
//    по порядку регистрации. Значит порт вообще не слушается, пока инициализатор ждёт БД, катит миграции и
//    сеет клиентов: отвечает не 503, а connection refused — молчит и /health/ready, и /health/live (проверено
//    запуском с недоступной базой: за 25 секунд ни строки «Now listening», curl не устанавливает соединение).
//    Дефолтный livenessProbe (periodSeconds 10, failureThreshold 3) при таком старте убьёт контейнер посреди
//    первой миграции и будет делать это по кругу. Спасает только startupProbe: пока он не прошёл, kubelet не
//    запускает ни liveness, ни readiness. Его бюджет (failureThreshold × periodSeconds) обязан покрывать
//    Startup:DatabaseWaitTimeoutSeconds (по умолчанию 60 с, см. DatabaseReadiness) ПЛЮС время миграций,
//    с запасом на холодный старт узла.
//
// 2) timeoutSeconds у проб — не меньше 3. Внутренний таймаут проверки ниже равен 2 с, и он имеет смысл
//    только пока kubelet готов ждать дольше: с дефолтным timeoutSeconds: 1 он оборвёт сокет раньше, чем
//    проверка успеет вернуть честный 503, и в событиях пода вместо причины будет «probe timed out».
builder.Services.AddHealthChecks()
    // 2 с — верхняя граница ожидания ответа от БД, парная к timeoutSeconds ≥ 3 в манифесте (п. 2 выше).
    // Таймаут именно здесь: kubelet свой timeoutSeconds считает от сокета, а зависший запрос к БД
    // должен закончиться честным 503, а не удержанием пробы до её собственного таймаута.
    .AddCheck<ReadinessHealthCheck>("ready", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

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

// AllowAnonymous стоит явно: фиксирует, что пробы не должны зависеть от будущих политик авторизации,
// как это уже требуется во Flow.Api с его FallbackPolicy. Ответ — одно слово Healthy/Unhealthy.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapGet("/", () => "Flow.Auth");
app.MapRazorPages();
app.MapControllers();

app.Run();

static void ConfigureKeys(OpenIddictServerBuilder options, AuthOptions auth, IHostEnvironment environment, ILoggingBuilder logging)
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

/// <summary>Для WebApplicationFactory в Flow.Auth.Tests.</summary>
public partial class Program;
