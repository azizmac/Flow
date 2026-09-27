using Flow.Application.Abstractions;
using Flow.Application.Exceptions;

namespace Flow.Application.Features.Tasks;

/// <summary>
/// Сохранение с рангом (docs/TZ_task_model.md §7): ключ вычисляется по соседям из БД, и два параллельных
/// запроса в одно место получают одинаковый. Unique (BoardId, Rank) отвергает второй — тогда ранг считается
/// заново по свежим соседям и сохранение повторяется, до трёх попыток. Откат транзакции оставляет сущности
/// в прежнем состоянии трекера, поэтому повторное SaveChanges отправит ту же правку с новым ключом.
/// </summary>
internal static class TaskRanks
{
    private const int MaxAttempts = 3;

    public static async Task SaveAsync(IUnitOfWork unitOfWork, Func<Task> reassignRank, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (RankConflictException) when (attempt < MaxAttempts)
            {
                await reassignRank();
            }
        }
    }
}
