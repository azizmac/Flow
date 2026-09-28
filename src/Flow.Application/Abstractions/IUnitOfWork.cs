namespace Flow.Application.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Забывает несохранённые правки и накопленную очередь индексации: хендлер, который коммитит несколько агрегатов
    /// по очереди (перенос конфигурации в проекты), не должен унести в следующий коммит полупримененный отказ.
    /// </summary>
    void DiscardChanges();

    /// <summary>Несколько SaveChangesAsync одной транзакцией: всё или ничего (запись в два шага — обмен имён статусов).</summary>
    Task InTransactionAsync(Func<Task> action, CancellationToken cancellationToken);
}
