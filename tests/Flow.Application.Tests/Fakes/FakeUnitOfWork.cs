using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    /// <summary>Исключения, которые бросят следующие сохранения по очереди — например, RankConflictException для проверки повтора.</summary>
    public Queue<Exception> Failures { get; } = new();

    public int SaveCount { get; private set; }

    public int DiscardCount { get; private set; }

    public void DiscardChanges() => DiscardCount++;

    public Task InTransactionAsync(Func<Task> action, CancellationToken cancellationToken) => action();

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Failures.TryDequeue(out var failure) ? Task.FromException(failure) : Task.CompletedTask;
    }
}
