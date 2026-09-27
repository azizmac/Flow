using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Infrastructure.Persistence.Configurations;
using Npgsql;
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

        try
        {
            await SaveCoreAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == TaskItemConfiguration.RankIndexName)
        {
            // Два запроса вычислили один ранг (docs/TZ_task_model.md §7): хендлер пересчитает ключ и повторит.
            throw new RankConflictException(ex);
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
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
            // Перестановка в ручном порядке — положение задачи, а не её изменение (§7): одна смена ранга дату не двигает.
            if (entry.State == EntityState.Modified
                && entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(TaskItem.Rank)))
            {
                entry.Entity.Touch(now);
            }
        }
    }
}
