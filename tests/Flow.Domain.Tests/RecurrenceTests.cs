using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Повторения (docs/TZ_task_model.md §9): даты правила, последний день месяца, 29 февраля, окно генерации и пауза.</summary>
public class RecurrenceTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static List<DateOnly> Dates(RecurrenceRule rule, DateOnly startsOn, DateOnly from, DateOnly to) =>
        rule.Occurrences(startsOn, from, to).ToList();

    [Fact]
    public void Daily_With_Interval_Counts_From_The_Start()
    {
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Daily, 3);

        Assert.Equal([Monday.AddDays(3), Monday.AddDays(6), Monday.AddDays(9)], Dates(rule, Monday, Monday.AddDays(1), Monday.AddDays(10)));
    }

    [Fact]
    public void Weekly_Every_Other_Week_On_Chosen_Days()
    {
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Weekly, 2, [DayOfWeek.Monday, DayOfWeek.Friday]);

        Assert.Equal([Monday, Monday.AddDays(4), Monday.AddDays(14), Monday.AddDays(18)], Dates(rule, Monday, Monday, Monday.AddDays(20)));
        // Без дней — день недели даты начала.
        Assert.Equal([Monday.AddDays(2), Monday.AddDays(9)], Dates(RecurrenceRule.Create(RecurrenceFrequency.Weekly), Monday.AddDays(2), Monday, Monday.AddDays(13)));
    }

    [Fact]
    public void Monthly_Last_Day_And_Day_Beyond_Month_Length()
    {
        var last = RecurrenceRule.Create(RecurrenceFrequency.Monthly, monthDay: RecurrenceRule.LastDayOfMonth);
        Assert.Equal([new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)],
            Dates(last, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31)));

        var thirtyFirst = RecurrenceRule.Create(RecurrenceFrequency.Monthly, 2, monthDay: 31);
        Assert.Equal([new DateOnly(2027, 1, 31), new DateOnly(2027, 3, 31), new DateOnly(2027, 5, 31), new DateOnly(2027, 7, 31)],
            Dates(thirtyFirst, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), new DateOnly(2027, 8, 30)));
        Assert.Equal([new DateOnly(2027, 4, 30)], Dates(RecurrenceRule.Create(RecurrenceFrequency.Monthly, monthDay: 31),
            new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 30)));
    }

    [Fact]
    public void Yearly_On_February_29_Falls_Back_In_Common_Years()
    {
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Yearly);

        Assert.Equal([new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28), new DateOnly(2030, 2, 28)],
            Dates(rule, new DateOnly(2028, 2, 29), new DateOnly(2028, 1, 1), new DateOnly(2030, 12, 31)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(100, null)]
    [InlineData(1, 0)]
    [InlineData(1, 32)]
    public void Invalid_Rules_Are_Rejected(int interval, int? monthDay) =>
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Create(RecurrenceFrequency.Monthly, interval, monthDay: monthDay));

    [Fact]
    public void Due_Dates_Respect_Lead_Days_End_Catch_Up_Window_And_Progress()
    {
        var board = Board.Create("Проект", "REC");
        var task = board.CreateTask("Отчёт");
        var today = new DateOnly(2026, 9, 20);
        var recurrence = TaskRecurrence.Create(task, Guid.NewGuid(), RecurrenceRule.Create(RecurrenceFrequency.Daily), today.AddDays(-30), today.AddDays(2),
            leadDays: 5, dueOffsetDays: 1, copyAssignee: true, copyChecklist: true);

        // Не старше недели назад, не дальше даты окончания.
        var due = recurrence.DueDates(today);
        Assert.Equal(today.AddDays(-TaskRecurrence.CatchUpDays), due[0]);
        Assert.Equal(today.AddDays(2), due[^1]);

        recurrence.MarkGenerated(today);
        Assert.Equal([today.AddDays(1), today.AddDays(2)], recurrence.DueDates(today));

        recurrence.Pause("Автор деактивирован");
        Assert.Empty(recurrence.DueDates(today));
        Assert.False(recurrence.IsActive);
    }

    [Fact]
    public void Invalid_Settings_Are_Rejected()
    {
        var task = Board.Create("Проект", "REC").CreateTask("Отчёт");
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Daily);

        Assert.Throws<ArgumentException>(() => TaskRecurrence.Create(task, Guid.NewGuid(), rule, Monday, Monday.AddDays(-1), 0, null, false, false));
        Assert.Throws<ArgumentException>(() => TaskRecurrence.Create(task, Guid.NewGuid(), rule, Monday, null, TaskRecurrence.MaxLeadDays + 1, null, false, false));
        Assert.Throws<ArgumentException>(() => TaskRecurrence.Create(task, Guid.NewGuid(), rule, Monday, null, 0, -1, false, false));
    }
}
