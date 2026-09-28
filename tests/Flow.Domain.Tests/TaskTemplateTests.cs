using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Шаблон задачи (этап 3G): образец названия, лимиты, чек-лист и подзадачи.</summary>
public class TaskTemplateTests
{
    private static TaskTemplate New(string pattern = "Отчёт {n} за {date}") => TaskTemplate.Create(Guid.NewGuid(), "Отчёт", pattern, Guid.NewGuid(), 0);

    [Fact]
    public void Title_Pattern_Takes_Date_And_Next_Number()
    {
        var template = New();

        Assert.Equal("Отчёт 1 за 05.03.2026", template.RenderTitle(new DateOnly(2026, 3, 5)));
        template.MarkUsed();
        Assert.Equal("Отчёт 2 за 05.03.2026", template.RenderTitle(new DateOnly(2026, 3, 5)));
        Assert.Equal("Без подстановок", New("Без подстановок").RenderTitle(new DateOnly(2026, 3, 5)));
    }

    [Fact]
    public void Checklist_And_Subtasks_Are_Trimmed_And_Limited()
    {
        var template = New();

        template.SetChecklist([" один ", "", "два"]);
        template.SetSubtasks([new TaskTemplateSubtask(" Шаг ", null, ["  а ", " "])]);

        Assert.Equal(["один", "два"], template.Checklist);
        var sub = Assert.Single(template.Subtasks);
        Assert.Equal("Шаг", sub.Title);
        Assert.Equal(["а"], sub.Checklist);
        Assert.Throws<ArgumentException>(() => template.SetSubtasks(Enumerable.Range(0, TaskTemplate.MaxSubtasks + 1).Select(i => new TaskTemplateSubtask($"S{i}", null, []))));
        Assert.Throws<ArgumentException>(() => template.SetSubtasks([new TaskTemplateSubtask(" ", null, [])]));
        Assert.Throws<ArgumentException>(() => template.SetChecklist(Enumerable.Repeat("x", TaskItem.MaxChecklistItems + 1)));
        Assert.Throws<ArgumentException>(() => template.SetChecklist([new string('x', TaskChecklistItem.TextMaxLength + 1)]));
        Assert.Throws<ArgumentException>(() => template.Rename(new string('x', TaskTemplate.NameMaxLength + 1)));
        Assert.Throws<ArgumentException>(() => template.SetTitlePattern(" "));
    }
}
