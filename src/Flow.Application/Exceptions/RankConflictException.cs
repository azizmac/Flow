namespace Flow.Application.Exceptions;

/// <summary>
/// Два запроса одновременно вычислили один и тот же ранг в проекте, и unique (BoardId, Rank) отверг второй
/// (docs/TZ_task_model.md §7). Бросает Infrastructure из IUnitOfWork; хендлеры, которые пишут ранг,
/// пересчитывают его по свежим соседям и сохраняют заново (<see cref="Features.Tasks.TaskRanks"/>).
/// </summary>
public sealed class RankConflictException(Exception inner)
    : Exception("Another task took the same rank concurrently.", inner);
