using Amazon.Runtime;
using Amazon.S3;
using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Persistence.Repositories;
using Flow.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowInfrastructureServiceCollectionExtensions
{
    private const string PostgresConnectionStringName = "Postgres";

    /// <summary>
    /// Регистрирует Flow.Infrastructure: FlowDbContext на Npgsql (строка подключения — секция
    /// "ConnectionStrings:Postgres"), реализации репозиториев/UnitOfWork, объявленных
    /// в Flow.Application.Abstractions, и поисковую часть (секция "Search", см. AddFlowSearch).
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
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<ICodeRepositoryRepository, CodeRepositoryRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        AddAttachments(services, configuration);

        // Поисковый индекс: очередь нужна хендлерам всегда (при Search:Enabled=false она молча
        // ничего не пишет), поэтому регистрируется здесь, а не только из Flow.Api/Program.cs.
        services.AddFlowSearch(configuration);
        return services;
    }

    /// <summary>
    /// Вложения: секция "Attachments" (лимиты) и "S3" (хранилище). Клиент S3 — синглтон: он потокобезопасен
    /// и держит свой пул соединений, создавать его на запрос незачем.
    /// </summary>
    private static void AddAttachments(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AttachmentOptions>(configuration.GetSection(AttachmentOptions.SectionName));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AttachmentOptions>>().Value);

        services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<S3Options>>().Value);

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<S3Options>();

            // ForcePathStyle обязателен для MinIO и любого не-AWS хранилища: адресация по поддомену
            // бакета там не работает.
            var config = new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                ForcePathStyle = true,
                UseHttp = !options.UseSsl,
                // Контрольные суммы AWS v4 не понимает часть S3-совместимых хранилищ.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };

            return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
        });

        services.AddScoped<IFileStorage, S3FileStorage>();
    }
}
