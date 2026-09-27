using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskCodeAliasRepository(FlowDbContext db) : ITaskCodeAliasRepository
{
    public async Task<TaskCodeAlias?> GetAsync(string code, CancellationToken cancellationToken) =>
        await db.TaskCodeAliases.FindAsync([code.Trim().ToUpperInvariant()], cancellationToken);

    public void Add(TaskCodeAlias alias) => db.TaskCodeAliases.Add(alias);

    public void Remove(TaskCodeAlias alias) => db.TaskCodeAliases.Remove(alias);
}
