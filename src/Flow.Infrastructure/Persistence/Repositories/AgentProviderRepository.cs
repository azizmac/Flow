using Flow.Application.Abstractions;
using Flow.Domain.Entities.Agents;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class AgentProviderRepository(FlowDbContext db) : IAgentProviderRepository
{
    public async Task<IReadOnlyList<AgentProvider>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.AgentProviders.OrderBy(p => p.ProviderKey).ToListAsync(cancellationToken);

    public Task<AgentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.AgentProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> ExistsByKeyAsync(string providerKey, CancellationToken cancellationToken) =>
        db.AgentProviders.AnyAsync(p => p.ProviderKey == providerKey, cancellationToken);

    public void Add(AgentProvider provider) => db.AgentProviders.Add(provider);

    public void Remove(AgentProvider provider) => db.AgentProviders.Remove(provider);

    public Task<AgentSettings?> GetSettingsAsync(CancellationToken cancellationToken) =>
        db.AgentSettings.FirstOrDefaultAsync(s => s.Id == AgentSettings.SingletonId, cancellationToken);

    public void AddSettings(AgentSettings settings) => db.AgentSettings.Add(settings);
}
