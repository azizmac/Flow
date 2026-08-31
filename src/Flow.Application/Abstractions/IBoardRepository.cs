using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Реализуется в Flow.Infrastructure (EF Core). Application не знает про DbContext/Npgsql —
/// только про то, какие операции над доской ему нужны.
/// </summary>
public interface IBoardRepository
{
    /// <summary>Доска вместе со статусами, отслеживаемая (для последующего изменения).</summary>
    Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Board>> GetAllAsync(CancellationToken cancellationToken);

    void Add(Board board);

    /// <summary>Удаление каскадное на уровне БД (Statuses/TaskItems доски удаляются вместе с ней).</summary>
    void Remove(Board board);
}
