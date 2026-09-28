using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;
using Flow.Application.Features.Tasks.Recurrence;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;
using SharedFrequency = Flow.Shared.Contracts.Tasks.RecurrenceFrequency;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Повторяющиеся задачи в Application (docs/TZ_task_model.md §9): правило на образце, копии с полями образца,
/// идемпотентность, окно пропусков, пауза при деактивированном авторе, права. Сами даты правила — Flow.Domain.Tests.
/// </summary>
public class TaskRecurrenceFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;
    private static readonly DateOnly Today = new(2026, 9, 20);

    private static async Task<(BoardResponse Board, TaskResponse Template)> TemplateAsync(IMediator mediator)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Регулярные", "REC"), CancellationToken.None)).Response!;
        var template = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Еженедельный отчёт", "Собрать цифры", null,
            Priority: Flow.Domain.Entities.TaskPriority.High), CancellationToken.None))!;
        return (board, template);
    }

    private static TaskRecurrenceRequest Daily(DateOnly startsOn, int lead = 0, int? due = 2) =>
        new(SharedFrequency.Daily, 1, startsOn, LeadDays: lead, DueOffsetDays: due);

    private static async Task<Guid> RuleIdAsync(IMediator mediator, Guid templateId) =>
        (await mediator.Send(new RecurrenceActiveIdsQuery(), CancellationToken.None)).Single();

    [Fact]
    public async Task Generates_Copies_From_The_Template_Once_Per_Date()
    {
        var (mediator, _, tasks, _) = TestMediatorFactory.Create();
        var (board, template) = await TemplateAsync(mediator);
        await mediator.Send(new TaskChecklistAddCommand(Owner, template.Id, "Выгрузить"), CancellationToken.None);
        await mediator.Send(new TaskAssignCommand(Owner, template.Id, Owner), CancellationToken.None);
        await mediator.Send(new TaskRecurrenceSetCommand(Owner, template.Id, Daily(Today.AddDays(-1), lead: 1)), CancellationToken.None);
        var ruleId = await RuleIdAsync(mediator, template.Id);

        Assert.Equal(3, await mediator.Send(new RecurrenceGenerateCommand(ruleId, Today), CancellationToken.None));
        Assert.Equal(0, await mediator.Send(new RecurrenceGenerateCommand(ruleId, Today), CancellationToken.None));

        var copies = (await tasks.SearchAsync(new Abstractions.TaskListFilter(BoardId: board.Id), CancellationToken.None))
            .Where(t => t.Id != template.Id).OrderBy(t => t.DueDate).ToList();
        Assert.Equal(3, copies.Count);
        var first = copies[0];
        Assert.Equal(("Еженедельный отчёт", "Собрать цифры", Flow.Domain.Entities.TaskPriority.High, (Guid?)Owner, (Guid?)Owner),
            (first.Title, first.Description, first.Priority, first.AssigneeId, first.CreatedById));
        Assert.Equal(Today.AddDays(1), first.DueDate);
        Assert.Equal(("Выгрузить", false), (first.Checklist.Single().Text, first.Checklist.Single().IsDone));
        Assert.Equal(board.Statuses.Single(s => s.IsInitial).Id, first.StatusId);

        var links = (await mediator.Send(new TaskLinkListQuery(Owner, first.Id), CancellationToken.None))!;
        Assert.Contains(links, l => l.Type == Flow.Shared.Contracts.Tasks.TaskLinkType.Clones && l.Outward && l.Other.Id == template.Id);
        var created = Assert.Single((await mediator.Send(new TaskActivityListQuery(Owner, first.Id), CancellationToken.None))!);
        Assert.Equal("по расписанию", created.NewValue);

        // Удалённая копия не возвращается: дата уже занята вхождением.
        await mediator.Send(new TaskDeleteCommand(Owner, first.Id), CancellationToken.None);
        Assert.Equal(0, await mediator.Send(new RecurrenceGenerateCommand(ruleId, Today), CancellationToken.None));
    }

    [Fact]
    public async Task After_Downtime_Only_The_Last_Week_Is_Caught_Up()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, template) = await TemplateAsync(mediator);
        await mediator.Send(new TaskRecurrenceSetCommand(Owner, template.Id, Daily(Today.AddDays(-30))), CancellationToken.None);

        Assert.Equal(TaskRecurrence.CatchUpDays + 1, await mediator.Send(new RecurrenceGenerateCommand(await RuleIdAsync(mediator, template.Id), Today), CancellationToken.None));
    }

    [Fact]
    public async Task Deactivated_Author_Pauses_The_Rule_Until_Someone_Resumes_It()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var author = User.Create("author", "author@example.com", "Автор", "Правил");
        author.ChangeRole(UserRole.Admin);
        author.MarkActive();
        users.Add(author);
        var (_, template) = await TemplateAsync(mediator);
        await mediator.Send(new TaskRecurrenceSetCommand(author.Id, template.Id, Daily(Today)), CancellationToken.None);
        var ruleId = await RuleIdAsync(mediator, template.Id);
        await mediator.Send(new UserDeactivateCommand(Owner, author.Id), CancellationToken.None);

        Assert.Equal(0, await mediator.Send(new RecurrenceGenerateCommand(ruleId, Today), CancellationToken.None));
        var paused = (await mediator.Send(new TaskRecurrenceGetQuery(Owner, template.Id), CancellationToken.None))!;
        Assert.False(paused.IsActive);
        Assert.NotNull(paused.LastError);

        var resumed = (await mediator.Send(new TaskRecurrenceSetCommand(Owner, template.Id, Daily(Today)), CancellationToken.None))!;
        Assert.Equal((true, Owner, (string?)null), (resumed.IsActive, resumed.CreatedById, resumed.LastError));
    }

    [Fact]
    public async Task Rule_Is_Validated_Previewed_And_Removed()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, template) = await TemplateAsync(mediator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new TaskRecurrenceSetCommand(Owner, template.Id, new TaskRecurrenceRequest(SharedFrequency.Weekly, 0, Today)), CancellationToken.None));

        var weekly = new TaskRecurrenceRequest(SharedFrequency.Weekly, 1, new DateOnly(2030, 1, 7), [DayOfWeek.Monday, DayOfWeek.Thursday]);
        var preview = (await mediator.Send(new TaskRecurrencePreviewQuery(Owner, template.Id, weekly, 3), CancellationToken.None))!;
        Assert.Equal([new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 10), new DateOnly(2030, 1, 14)], preview);

        await mediator.Send(new TaskRecurrenceSetCommand(Owner, template.Id, weekly), CancellationToken.None);
        Assert.True(await mediator.Send(new TaskRecurrenceDeleteCommand(Owner, template.Id), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskRecurrenceGetQuery(Owner, template.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Rule_Needs_Edit_Rights_On_The_Template()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        users.Add(member);
        var (_, template) = await TemplateAsync(mediator);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new TaskRecurrenceSetCommand(member.Id, template.Id, Daily(Today)), CancellationToken.None));
    }
}
