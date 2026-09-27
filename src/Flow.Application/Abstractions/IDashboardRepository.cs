using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Дашборды (docs/TZ_task_views.md §8); дашборд грузится вместе с виджетами и отслеживается.</summary>
public interface IDashboardRepository
{
    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Свои и общие, по имени.</summary>
    Task<IReadOnlyList<Dashboard>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Свои — чтобы снять флаг «по умолчанию» с остальных.</summary>
    Task<IReadOnlyList<Dashboard>> GetOwnedAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Dashboard dashboard);

    void Remove(Dashboard dashboard);
}

/// <summary>Поле разбивки виджета «Разбивка» (docs/TZ_task_views.md §8).</summary>
public enum TaskGroupField { Status, Assignee, Priority, Type, Board, CustomField }

/// <summary>Группа разбивки: Key — Id (статуса, человека, типа, проекта, варианта) или число приоритета строкой; null — «не задано».</summary>
public sealed record TaskGroupCount(string? Key, int Count);

/// <summary>Создано и закрыто по дням (UTC) — для виджета «Создано / закрыто».</summary>
public sealed record TaskDailyCounts(IReadOnlyDictionary<DateOnly, int> Created, IReadOnlyDictionary<DateOnly, int> Closed);
