using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Слияние, разделение и перенос задачи (docs/TZ_task_model.md §6) и поиск по коду — живому или прежнему.
/// Отказ по инвариантам (уровни подзадач, перенос в тот же проект, нет подходящего типа) — 400 { message }.
/// </summary>
[ApiController]
public class TaskRestructureController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>По коду; прежний код переехавшей задачи тоже находит её.</summary>
    [HttpGet("tasks/by-code/{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskGetByCodeQuery(actor.Require(), code), cancellationToken) is { } task ? Ok(task) : NotFound();

    /// <summary>Влить задачу в targetId; ответ — основная задача.</summary>
    [HttpPost("tasks/{id:guid}/merge")]
    public Task<IActionResult> Merge(Guid id, MergeTaskRequest request, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new TaskMergeCommand(actor.Require(), id, request.TargetId), cancellationToken));

    /// <summary>1–20 новых задач; 201 со списком.</summary>
    [HttpPost("tasks/{id:guid}/split")]
    public async Task<IActionResult> Split(Guid id, SplitTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await mediator.Send(new TaskSplitCommand(actor.Require(), id, request.Parts ?? []), cancellationToken);
            return created is null ? NotFound() : StatusCode(StatusCodes.Status201Created, created);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Что сделает перенос: карты статусов и типов, теряемые значения полей, проблемы. POST — карты в теле.</summary>
    [HttpPost("tasks/{id:guid}/move/preview")]
    public Task<IActionResult> MovePreview(Guid id, MoveTaskRequest request, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new TaskMovePreviewQuery(actor.Require(), id, request.BoardId, request.StatusMap, request.TypeMap), cancellationToken));

    /// <summary>Перенос с поддеревом; ответ — задача с новым кодом.</summary>
    [HttpPost("tasks/{id:guid}/move")]
    public Task<IActionResult> Move(Guid id, MoveTaskRequest request, CancellationToken cancellationToken) =>
        Send(() => mediator.Send(new TaskMoveCommand(actor.Require(), id, request.BoardId, request.StatusMap, request.TypeMap), cancellationToken));

    private async Task<IActionResult> Send<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            return await action() is { } result ? Ok(result) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
