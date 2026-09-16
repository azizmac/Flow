using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flow.Infrastructure.Persistence;

public static class FlowDatabase
{
    /// <summary>
    /// Применяет миграции и перезагружает каталог типов Npgsql. Второе обязательно из-за pgvector:
    /// <c>CREATE EXTENSION vector</c> выполняется миграцией, а типы <c>vector</c> и <c>halfvec</c>
    /// Npgsql читает из БД один раз, при первом подключении. На свежей базе он подключается раньше
    /// миграции, и без перезагрузки запись вектора падает с «Cannot resolve halfvec» до перезапуска процесса.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        await scopedServices.GetRequiredService<FlowDbContext>().Database.MigrateAsync(cancellationToken);
        await scopedServices.GetRequiredService<NpgsqlDataSource>().ReloadTypesAsync();
    }
}
