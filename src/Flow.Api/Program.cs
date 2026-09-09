using Flow.Api.Auth;
using Flow.Api.Bootstrap;
using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowInfrastructure(builder.Configuration);
builder.Services.AddFlowApplication();
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());

// ---- Аутентификация: Bearer JWT от Flow.Auth (docs/TZ_auth.md) ----
// Discovery/JWKS — по Auth:BaseUrl (в Docker внутренний адрес), issuer в токене — Auth:Issuer (внешний).
var jwt = builder.Configuration.GetSection(JwtAuthOptions.SectionName).Get<JwtAuthOptions>() ?? new JwtAuthOptions();
if (string.IsNullOrWhiteSpace(jwt.BaseUrl))
    throw new InvalidOperationException("Auth:BaseUrl is not configured — Flow.Api cannot validate tokens without Flow.Auth.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwt.BaseUrl;
        options.Audience = JwtAuthOptions.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        // Оставляем имена claims как в токене (sub, name, email), без переписывания в схемы XML.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidIssuer = string.IsNullOrWhiteSpace(jwt.Issuer) ? jwt.BaseUrl : jwt.Issuer;
        options.TokenValidationParameters.NameClaimType = "name";
    });

// Закрыто всё; исключения — явные [AllowAnonymous] (сейчас только GET /).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorAccessor, ClaimsActorAccessor>();

builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddHostedService<BootstrapOwnerSeeder>();

// Flow.Client (Blazor WASM) хостится на другом origin (dev-сервер на :5016/:7062), поэтому браузеру нужен CORS.
// Список origin'ов — секция "Cors:Origins" (appsettings / env Cors__Origins__0).
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors(clientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Flow.Api").AllowAnonymous();

app.MapControllers();

app.Run();

/// <summary>Для WebApplicationFactory в Flow.Api.Tests.</summary>
public partial class Program;
