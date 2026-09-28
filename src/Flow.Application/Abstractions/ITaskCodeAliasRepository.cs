using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Прежние коды переехавших задач (docs/TZ_task_model.md §6). Поиск по коду — ITaskItemRepository.GetByCodeAsync.</summary>
public interface ITaskCodeAliasRepository
{
    Task<TaskCodeAlias?> GetAsync(string code, CancellationToken cancellationToken);

    void Add(TaskCodeAlias alias);

    void Remove(TaskCodeAlias alias);
}
