using Flow.Auth.Options;
using Flow.Auth.Security;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Flow.Api.OpenApi;

/// <summary>
/// Swagger для JSON-API: документ OpenAPI строит встроенный генератор ASP.NET Core (Microsoft.AspNetCore.OpenApi),
/// Swashbuckle отдаёт только страницу UI. Документ — <c>/swagger/v1/swagger.json</c>, страница — <c>/swagger</c>.
///
/// Анонимно не открывается ни то, ни другое: FallbackPolicy закрывает и эндпоинт документа, и статику UI —
/// UI это middleware после UseAuthorization, а политика по умолчанию действует и на запросы без эндпоинта.
/// Без сессии браузер уводят на /account/login, после входа он возвращается на /swagger.
///
/// Сессия страницы (cookie) до /api не доедет: там принимается только Bearer. Токен UI берёт сам кнопкой
/// Authorize — code + PKCE у OpenIddict на том же origin публичным клиентом flow-client. Для этого адрес
/// <c>{origin}/swagger/oauth2-redirect.html</c> обязан стоять в Auth:Client:RedirectUris, иначе OpenIddict
/// ответит invalid_request про redirect_uri. Вторая схема — готовый токен вставить руками.
/// </summary>
internal static class SwaggerSetup
{
    private const string DocumentName = "v1";
    private const string RoutePrefix = "swagger";
    private const string OAuth2Scheme = "oauth2";
    private const string BearerScheme = "bearer";

    public static IServiceCollection AddFlowSwagger(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Flow API",
                Version = DocumentName,
                Description = "JSON-API Flow. Все маршруты под /api и принимают только Bearer-токен (aud = flow-api)."
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[OAuth2Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Вход через OpenIddict: code + PKCE, публичный клиент flow-client.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        // Относительные адреса: Swagger UI достраивает их от origin страницы, а Auth-модуль
                        // живёт в этом же хосте — так не нужно знать внешний адрес за ingress.
                        AuthorizationUrl = new Uri("/connect/authorize", UriKind.Relative),
                        TokenUrl = new Uri("/connect/token", UriKind.Relative),
                        Scopes = new Dictionary<string, string> { [AuthConstants.ApiScope] = "Доступ к Flow API" }
                    }
                }
            };
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Готовый access token от /connect/token."
            };

            // Два требования в списке — это «или»: хватит любой из схем. Закрыто всё API, поэтому требование
            // глобальное, а не на каждой операции.
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(OAuth2Scheme, document)] = [AuthConstants.ApiScope]
            });
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });

            return Task.CompletedTask;
        }));

        return services;
    }

    /// <summary>Ставить после UseAuthorization: иначе FallbackPolicy не накроет статику UI.</summary>
    public static WebApplication UseFlowSwagger(this WebApplication app)
    {
        app.MapOpenApi($"/{RoutePrefix}/{{documentName}}/swagger.json");

        var clientId = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Client.ClientId;
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = RoutePrefix;
            options.DocumentTitle = "Flow API";
            options.SwaggerEndpoint($"/{RoutePrefix}/{DocumentName}/swagger.json", "Flow API v1");
            options.OAuthClientId(clientId);
            options.OAuthScopes(AuthConstants.ApiScope);
            options.OAuthUsePkce();
        });

        return app;
    }
}
