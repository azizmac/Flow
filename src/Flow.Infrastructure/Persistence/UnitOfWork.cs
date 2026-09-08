using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Persistence;

public sealed class UnitOfWork(FlowDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
