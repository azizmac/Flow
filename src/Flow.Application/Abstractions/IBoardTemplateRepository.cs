using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Сохранённые шаблоны проектов (этап 3F); встроенные живут в коде, здесь их нет.</summary>
public interface IBoardTemplateRepository
{
    Task<BoardTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Все сохранённые, по имени.</summary>
    Task<IReadOnlyList<BoardTemplate>> GetAllAsync(CancellationToken cancellationToken);

    void Add(BoardTemplate template);

    void Remove(BoardTemplate template);
}
