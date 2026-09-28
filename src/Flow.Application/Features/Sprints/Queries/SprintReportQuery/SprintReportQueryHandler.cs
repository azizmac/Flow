using System.Globalization;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.SprintReportQuery;

internal sealed class SprintReportQueryHandler(
    ISprintRepository sprints,
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IProjectAccess projectAccess)
    : IRequestHandler<SprintReportQuery, SprintReportResponse?>
{
    private static readonly TaskActivityType[] BurndownTypes =
        [TaskActivityType.SprintChanged, TaskActivityType.StatusChanged, TaskActivityType.StoryPointsChanged];

    public async Task<SprintReportResponse?> Handle(SprintReportQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var sprint = await sprints.GetByIdAsync(request.SprintId, cancellationToken);
        if (sprint is null || !(await projectAccess.GetAsync(actor, sprint.BoardId, cancellationToken)).CanView)
            return null;
        if (await boards.GetByIdAsync(sprint.BoardId, cancellationToken) is not { } board)
            return null;

        var finals = board.Statuses.Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var byId = (await tasks.GetTreeAsync(board.Id, null, cancellationToken)).ToDictionary(e => e.Task.Id, e => e.Task);

        var committed = sprint.Commitments.Where(c => c.Kind == SprintCommitmentKind.Committed).ToDictionary(c => c.TaskId, c => c.StoryPoints);
        var inSprint = byId.Values.Where(t => t.SprintId == sprint.Id).ToList();
        var done = inSprint.Where(t => finals.Contains(t.StatusId)).ToDictionary(t => t.Id, t => t.StoryPoints);
        var notDone = sprint.IsCompleted
            ? sprint.Commitments.Where(c => c.Kind == SprintCommitmentKind.CarriedOver).ToDictionary(c => c.TaskId, c => c.StoryPoints)
            : inSprint.Where(t => !finals.Contains(t.StatusId)).ToDictionary(t => t.Id, t => t.StoryPoints);
        var added = done.Concat(notDone).Where(p => !committed.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);

        static SprintReportTotals Totals(IReadOnlyDictionary<Guid, decimal?> set) => new(set.Count, set.Values.Sum(p => p ?? 0));

        var rows = committed.Keys.Union(done.Keys).Union(notDone.Keys)
            .Select(id =>
            {
                var task = byId.GetValueOrDefault(id);
                var points = committed.TryGetValue(id, out var snap) ? snap : notDone.TryGetValue(id, out var nd) ? nd : task?.StoryPoints;
                return new SprintReportTask(id, task?.Code.Value, task?.Title, points, committed.ContainsKey(id), done.ContainsKey(id));
            })
            .OrderBy(r => r.Done).ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        return new SprintReportResponse(
            sprint.ToResponse(),
            Totals(committed),
            Totals(added),
            Totals(done),
            Totals(notDone),
            rows,
            await BurndownAsync(sprint, byId, finals, Totals(committed).Points, cancellationToken));
    }

    /// <summary>
    /// Остаток на конец каждого дня: берём нынешнее состояние задач и «откатываем» журнал назад по дням —
    /// снимок на прошлый момент получается без хранения истории состояний. Кандидаты — задачи, которые сейчас в
    /// спринте или когда-либо в него входили/из него уходили (SprintChanged с его Id).
    /// </summary>
    private async Task<IReadOnlyList<BurndownPoint>> BurndownAsync(
        Sprint sprint, IReadOnlyDictionary<Guid, TaskItem> byId, IReadOnlySet<Guid> finals, decimal committedPoints, CancellationToken cancellationToken)
    {
        if (sprint.StartDate is not { } start || sprint.EndDate is not { } end || sprint.State == Domain.Entities.SprintState.Planned)
            return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last = new[] { end, today, sprint.CompletedAt is { } c ? DateOnly.FromDateTime(c) : end }.Min();
        if (last < start)
            return [];

        var candidates = (await activities.GetTaskIdsWithValueAsync(TaskActivityType.SprintChanged, sprint.Id.ToString(), cancellationToken))
            .Concat(byId.Values.Where(t => t.SprintId == sprint.Id).Select(t => t.Id))
            .Concat(sprint.Commitments.Select(x => x.TaskId))
            .Where(byId.ContainsKey)
            .ToHashSet();

        var state = candidates.ToDictionary(id => id, id => (Sprint: byId[id].SprintId, Status: byId[id].StatusId, Points: byId[id].StoryPoints));
        var journal = (await activities.GetByTaskIdsAsync(candidates, BurndownTypes, cancellationToken))
            .OrderByDescending(a => a.CreatedAt).ToList();

        var completedOn = sprint.CompletedAt is { } done ? DateOnly.FromDateTime(done) : (DateOnly?)null;
        var carried = sprint.Commitments.Where(x => x.Kind == SprintCommitmentKind.CarriedOver).Select(x => x.TaskId).ToHashSet();
        var totalDays = Math.Max(1, end.DayNumber - start.DayNumber);
        decimal Ideal(DateOnly day) => Math.Round(committedPoints * Math.Max(0, end.DayNumber - day.DayNumber) / totalDays, 1);

        var points = new List<BurndownPoint>();
        for (var day = end; day > last; day = day.AddDays(-1))
            points.Add(new BurndownPoint(day, null, Ideal(day)));

        var next = 0;
        for (var day = last; day >= start; day = day.AddDays(-1))
        {
            // Состояние на конец дня: откатываем всё, что случилось после его полуночи.
            var boundary = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            for (; next < journal.Count && journal[next].CreatedAt >= boundary; next++)
                Undo(journal[next], state);

            // День завершения: перенос незакрытых случился в этот же день, но остаток на конец спринта — это они же.
            var remaining = state
                .Where(s => (s.Value.Sprint == sprint.Id || (day == completedOn && carried.Contains(s.Key))) && !finals.Contains(s.Value.Status))
                .Sum(s => s.Value.Points ?? 0);
            points.Add(new BurndownPoint(day, remaining, Ideal(day)));
        }

        points.Reverse();
        return points;
    }

    private static void Undo(TaskActivity entry, Dictionary<Guid, (Guid? Sprint, Guid Status, decimal? Points)> state)
    {
        var s = state[entry.TaskId];
        state[entry.TaskId] = entry.Type switch
        {
            TaskActivityType.SprintChanged => s with { Sprint = Guid.TryParse(entry.OldValue, out var sprintId) ? sprintId : null },
            TaskActivityType.StatusChanged when Guid.TryParse(entry.OldValue, out var status) => s with { Status = status },
            TaskActivityType.StoryPointsChanged => s with
            {
                Points = decimal.TryParse(entry.OldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : null
            },
            _ => s
        };
    }
}
