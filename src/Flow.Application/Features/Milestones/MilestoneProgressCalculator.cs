using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Milestones;
using SharedState = Flow.Shared.Contracts.Milestones.MilestoneState;

namespace Flow.Application.Features.Milestones;

/// <summary>
/// Прогресс вех (docs/TZ_task_views.md §6): счётчики — один GROUP BY в репозитории, прогноз — здесь: средняя
/// скорость закрытия за <see cref="ForecastWindowDays"/> дней → сколько дней нужно на остаток. Все ответы про вехи
/// (список, одна веха, ответы команд) идут через этот класс — так прогресс в них не расходится.
/// </summary>
internal sealed class MilestoneProgressCalculator(ITaskItemRepository tasks)
{
    public const int ForecastWindowDays = 14;

    public async Task<IReadOnlyList<MilestoneResponse>> ToResponsesAsync(IReadOnlyList<Milestone> milestones, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var counts = await tasks.CountByMilestonesAsync(milestones.Select(m => m.Id).ToList(), today, now.AddDays(-ForecastWindowDays), cancellationToken);
        return milestones.Select(m => ToResponse(m, counts.GetValueOrDefault(m.Id), today)).ToList();
    }

    public async Task<MilestoneResponse> ToResponseAsync(Milestone milestone, CancellationToken cancellationToken) =>
        (await ToResponsesAsync([milestone], cancellationToken))[0];

    public static MilestoneProgress Progress(MilestoneCounts c, DateOnly today)
    {
        var remaining = c.Total - c.Done;
        DateOnly? forecast = remaining > 0 && c.ClosedRecently > 0
            ? today.AddDays((int)Math.Ceiling(remaining * (double)ForecastWindowDays / c.ClosedRecently))
            : null;
        return new MilestoneProgress(c.Total, c.Done, c.InProgress, c.Points, c.DonePoints, c.Overdue, c.ClosedRecently, forecast);
    }

    private static MilestoneResponse ToResponse(Milestone m, MilestoneCounts counts, DateOnly today) => new(
        m.Id,
        m.BoardId,
        m.Name,
        m.Description,
        m.TargetDate,
        (SharedState)(int)m.State,
        m.ClosedAt,
        m.SortOrder,
        Progress(counts, today));
}
