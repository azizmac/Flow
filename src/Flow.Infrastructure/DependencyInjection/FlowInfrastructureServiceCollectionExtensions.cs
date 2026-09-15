using Flow.Application.Abstractions;
using Flow.Infrastructure.Auth;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowInfrastructureServiceCollectionExtensions
{
    private const string PostgresConnectionStringName = "Postgres";

    /// <summary>
    /// Регистрирует Flow.Infrastructure: FlowDbContext на Npgsql (строка подключения — секция
    /// "ConnectionStrings:Postgres"), реализации репозиториев/UnitOfWork, объявленных
    /// в Flow.Application.Abstractions, HTTP-клиент admin-API Flow.Auth (IAccountService, секция "Auth")
    /// и поисковую часть (секция "Search", см. AddFlowSearch).
    /// Использование в Flow.Api/Program.cs: builder.Services.AddFlowInfrastructure(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string \"{PostgresConnectionStringName}\" is not configured.");

        // Свой NpgsqlDataSource нужен ради pgvector: маппинг halfvec на Pgvector.HalfVector
        // регистрируется на двух уровнях — в ADO (здесь) и в EF (o.UseVector()). Плюс его же
        // перезагружает FlowDatabase.MigrateAsync после CREATE EXTENSION.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddDbContext<FlowDbContext>(options => options
            .UseNpgsql(dataSource, o => o.UseVector()));

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<ITaskItemRepository, TaskItemRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<ITaskActivityRepository, TaskActivityRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Поисковый индекс: очередь нужна хендлерам всегда (при Search:Enabled=false она молча
        // ничего не пишет), поэтому регистрируется здесь, а не только из Flow.Api/Program.cs.
        services.AddFlowSearch(configuration);

        // Служебные вызовы в Flow.Auth (admin-API /accounts): секция "Auth" — BaseUrl и клиент flow-api.
        // Отсутствие настроек проявится AuthUnavailableException при первом вызове, а не на старте: тесты
        // Infrastructure подменяют IAccountService и в Flow.Auth не ходят.
        services.Configure<AuthClientOptions>(configuration.GetSection(AuthClientOptions.SectionName));
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.AddSingleton<ClientCredentialsTokenProvider>();
        services.AddHttpClient<IAccountService, AuthAccountService>();

        return services;
    }
}
