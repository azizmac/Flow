using Flow.Application.Abstractions;
using Flow.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence;

internal sealed class UnitOfWork(FlowDbContext db, SearchIndexQueue searchQueue) : IUnitOfWork
{
    /// <summary>
    /// Обычный случай — один SaveChangesAsync. Если хендлер поставил что-то в очередь индексации,
    /// правка и запись очереди уходят одной транзакцией: очередь пишется сырым INSERT ... ON CONFLICT
    /// (свой запрос вне EF), поэтому транзакцию приходится открывать явно — иначе откат правки
    /// оставил бы в очереди источник, которого нет.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (!searchQueue.HasPending)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Внешняя транзакция (тесты, будущие сценарии) — просто дописываемся в неё.
        if (db.Database.CurrentTransaction is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            await searchQueue.FlushAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await searchQueue.FlushAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
