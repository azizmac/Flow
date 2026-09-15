using Flow.Application.Abstractions;
using Flow.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence;

/// <summary>
/// Единственная точка коммита. Если хендлер попутно поставил что-то в очередь поиска, коммит становится
/// транзакционным: сохранение сущностей и запись очереди уходят вместе или не уходят вовсе. Иначе
/// пришлось бы жить с «задача есть, в индексе её нет» и чинить это ночной сверкой.
/// <paramref name="searchQueue"/> с дефолтом null: без AddFlowSearch поиска в приложении просто нет.
/// </summary>
public sealed class UnitOfWork(FlowDbContext db, SearchIndexQueue? searchQueue = null) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (searchQueue is not { HasPending: true })
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Внешняя транзакция уже есть (тест или составная операция) — вкладываемся в неё, а не начинаем свою.
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
