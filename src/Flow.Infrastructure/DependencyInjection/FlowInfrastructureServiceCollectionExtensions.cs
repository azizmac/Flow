using Flow.Application.Abstractions;
using Flow.Infrastructure.Auth;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pgvector.EntityFrameworkCore;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowInfrastructureServiceCollectionExtensions
{
    private const string PostgresConnectionStringName = "Postgres";

    /// <summary>
    /// Регистрирует Flow.Infrastructure: FlowDbContext на Npgsql (строка подключения — секция
    /// "ConnectionStrings:Postgres"), реализации репозиториев/UnitOfWork, объявленных
    /// в Flow.Application.Abstractions, HTTP-клиент admin-API Flow.Auth (IAccountService, секция "Auth")
    /// и поиск (AddFlowSearch, секция "Search").
    /// Использование в Flow.Api/Program.cs: builder.Services.AddFlowInfrastructure(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string \"{PostgresConnectionStringName}\" is not configured.");

        // UseVector включает маппинг pgvector (halfvec(512) в "SearchChunks"); само расширение
        // в БД создаёт миграция AddSearchIndex.
        services.AddDbContext<FlowDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<ITaskItemRepository, TaskItemRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<ITaskActivityRepository, TaskActivityRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Служебные вызовы в Flow.Auth (admin-API /accounts): секция "Auth" — BaseUrl и клиент flow-api.
        // Отсутствие настроек проявится AuthUnavailableException при первом вызове, а не на старте: тесты
        // Infrastructure подменяют IAccountService и в Flow.Auth не ходят.
        services.Configure<AuthClientOptions>(configuration.GetSection(AuthClientOptions.SectionName));
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.AddSingleton<ClientCredentialsTokenProvider>();
        services.AddHttpClient<IAccountService, AuthAccountService>();

        // Поиск (docs/TZ_search_stage1-3.md): очередь индексации нужна хендлерам Boards/Tasks/Users всегда,
        // даже при Search:Enabled=false — тогда она просто ничего не пишет. Вызов идемпотентный,
        // Flow.Api/Program.cs дублирует его явно.
        services.AddFlowSearch(configuration);

        return services;
    }
}
