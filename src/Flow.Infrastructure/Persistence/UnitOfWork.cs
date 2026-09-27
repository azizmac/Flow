using Flow.Application.Abstractions;
using Flow.Domain.Entities;
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
        TouchModifiedTasks();

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

    /// <summary>
    /// TaskItem.UpdatedAt ставится здесь, а не в каждом доменном методе: новое поле задачи, забывшее «тронуть»
    /// дату, иначе тихо ломало бы сортировку «по изменению». Entries() сам вызывает DetectChanges, поэтому
    /// изменённые через методы сущности задачи уже помечены Modified.
    /// </summary>
    private void TouchModifiedTasks()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in db.ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.Touch(now);
        }
    }
}
