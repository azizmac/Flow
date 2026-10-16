using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Api.Mcp.Tools;
using Flow.Auth.Options;
using Flow.Auth.Security;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace Flow.Api.Mcp;

/// <summary>
/// MCP-сервер Flow (docs/MCP.md): Streamable HTTP на /mcp в том же хосте. Инструменты — тонкий слой над IMediator,
/// как контроллеры: права, видимость приватных проектов и журнал работают ровно так же, как в API, потому что
/// команды те же. Без состояния между запросами (Stateless): actor — claim sub токена текущего HTTP-запроса,
/// и балансировщику не нужна привязка к экземпляру.
/// </summary>
public static class McpSetup
{
    public const string Path = AuthConstants.McpPathPrefix;

    /// <summary>
    /// Enum строками и без null-полей: ответ читает модель, и «priority: High» ей понятнее, чем «priority: 3»,
    /// а пустые поля только съедают контекст.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IServiceCollection AddFlowMcp(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()?.Issuer
            ?? new AuthOptions().Issuer;

        services.AddScoped<McpContext>();

        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "flow",
                    Title = "Flow",
                    Version = typeof(McpSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0"
                };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<McpProjectTools>(JsonOptions)
            .WithTools<McpTaskTools>(JsonOptions)
            .WithTools<McpSearchTools>(JsonOptions);

        // Схема MCP только оформляет 401 (resource_metadata в WWW-Authenticate) и отдаёт метаданные
        // защищённого ресурса; сам токен проверяет OpenIddict Validation — та же, что у /api.
        services.AddAuthentication().AddMcp(AuthConstants.McpScheme, "Flow MCP", options =>
        {
            options.ForwardAuthenticate = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = AuthConstants.McpResource(issuer),
                AuthorizationServers = { issuer.TrimEnd('/') },
                ScopesSupported = [AuthConstants.ApiScope, "offline_access"],
                BearerMethodsSupported = ["header"],
                ResourceName = "Flow"
            };
        });

        return services;
    }

    public static IEndpointConventionBuilder MapFlowMcp(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapMcp(Path).RequireAuthorization();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Каталог полей FQL — из того же FqlFields, что у биндера и подсказок: разойтись им не с чем.</summary>
    private static readonly string Instructions = $"""
        Flow — трекер задач по проектам. Проект задаётся ключом (например, PROJ), задача — кодом (PROJ-142);
        Guid тоже принимаются. Статусы, типы задач и пользовательские поля у каждого проекта свои —
        их список отдаёт get_project. Исполнитель — username, «me» (текущий пользователь) или «none».
        Все действия выполняются от имени и с правами пользователя, которому выдан токен.

        Для отбора задач в search_tasks есть FQL (похож на JQL): условия через AND/OR/NOT, скобки, IN (…),
        IS EMPTY, сравнения = != < <= > >=, ~ для подстроки, ORDER BY поле [ASC|DESC]. Пример:
        project = PROJ AND assignee = me() AND statusCategory != done ORDER BY priority DESC
        Поля:
        {string.Join("\n", Flow.Application.Features.Tasks.Fql.FqlFields.All.Select(f => $"- {f.Name}: {f.Hint}"))}
        Для поиска по смыслу (задачи, комментарии, файлы, люди) — search.
        """;
}
