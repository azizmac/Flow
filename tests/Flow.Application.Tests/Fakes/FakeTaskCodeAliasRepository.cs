using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskCodeAliasRepository : ITaskCodeAliasRepository
{
    private readonly List<TaskCodeAlias> _aliases = [];

    public FakeTaskCodeAliasRepository(FakeTaskItemRepository tasks) =>
        tasks.AliasLookup = code => _aliases.SingleOrDefault(a => a.Code == code)?.TaskId;

    public IReadOnlyList<TaskCodeAlias> All => _aliases;

    public Task<TaskCodeAlias?> GetAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_aliases.SingleOrDefault(a => a.Code == code.Trim().ToUpperInvariant()));

    public void Add(TaskCodeAlias alias) => _aliases.Add(alias);

    public void Remove(TaskCodeAlias alias) => _aliases.Remove(alias);
}
