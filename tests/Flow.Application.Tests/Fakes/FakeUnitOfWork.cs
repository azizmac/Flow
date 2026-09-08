using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
