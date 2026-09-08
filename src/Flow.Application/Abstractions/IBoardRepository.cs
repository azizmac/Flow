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

    /// <summary>Ключ уникален на уровне БД (IX_Boards_Key); ожидает уже нормализованный ключ (см. Board.Key).</summary>
    Task<bool> ExistsByKeyAsync(string key, CancellationToken cancellationToken);

    void Add(Board board);

    /// <summary>
    /// Удаляет доску вместе со статусами и задачами. Асинхронный, потому что реализации может понадобиться
    /// догрузить задачи: у TaskItems FK на Statuses с Restrict, и удалять их нужно раньше статусов.
    /// </summary>
    Task RemoveAsync(Board board, CancellationToken cancellationToken);
}
