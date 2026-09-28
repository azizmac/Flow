using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardMembersQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Boards.Commands.StatusCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Commands.StatusReorderCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

[ApiController]
[Route("boards")]
public class BoardsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateBoard(CreateBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new BoardCreateCommand(actor.Require(), request.Name, request.Key, request.TemplateId),
                cancellationToken);

            if (result.IsKeyTaken)
                return Conflict(new { Message = result.ValidationError });

            var response = result.Response!;
            return CreatedAtAction(nameof(GetBoard), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBoards(CancellationToken cancellationToken)
    {
        var boards = await mediator.Send(new BoardListQuery(actor.Require()), cancellationToken);
        return Ok(boards);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBoard(Guid id, CancellationToken cancellationToken)
    {
        var board = await mediator.Send(new BoardGetQuery(actor.Require(), id), cancellationToken);
        return board is null ? NotFound() : Ok(board);
    }

    [HttpPatch("{id:guid}/name")]
    public async Task<IActionResult> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new BoardRenameCommand(actor.Require(), id, request.Name),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Добавить тип задачи; ответ — проект целиком (флаг «по умолчанию» мог переехать). 400 — занятое имя.</summary>
    [HttpPost("{id:guid}/task-types")]
    public async Task<IActionResult> CreateTaskType(Guid id, CreateTaskTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.IsDefined(request.Kind))
                return BadRequest(new { Message = $"Unknown task type kind {request.Kind}." });

            var response = await mediator.Send(
                new TaskTypeCreateCommand(actor.Require(), id, request.Name, request.Kind.ToDomainKind(), request.IsDefault),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>PATCH типа: имя, «по умолчанию» (только true), архив. 400 — нарушение инварианта или чужой тип.</summary>
    [HttpPatch("{id:guid}/task-types/{typeId:guid}")]
    public async Task<IActionResult> UpdateTaskType(Guid id, Guid typeId, UpdateTaskTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new TaskTypeUpdateCommand(actor.Require(), id, typeId, request.Name, request.IsDefault, request.IsArchived),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    // ---- Статусы (docs/TZ_workflow_config.md §1, этап 3A). Ответ — проект целиком: флаги могли переехать. ----

    /// <summary>Добавить статус в конец списка. 400 — занятое имя или неизвестный вид.</summary>
    [HttpPost("{id:guid}/statuses")]
    public async Task<IActionResult> CreateStatus(Guid id, CreateStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new StatusCreateCommand(actor.Require(), id, request.Name, request.Type?.ToDomainStatusType(), request.IsFinal),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>PATCH статуса: имя, вид, «финальный», «начальный» (только true). 400 — нарушение инварианта.</summary>
    [HttpPatch("{id:guid}/statuses/{statusId:guid}")]
    public async Task<IActionResult> UpdateStatus(Guid id, Guid statusId, UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new StatusUpdateCommand(actor.Require(), id, statusId, request.Name, request.IsFinal, request.IsInitial,
                    request.Type?.ToDomainStatusType(), request.ClearType, request.WipLimit, request.ClearWipLimit),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Удалить статус, переведя задачи в <paramref name="moveTo"/>. 400 — начальный, последний финальный, moveTo не из проекта.</summary>
    [HttpDelete("{id:guid}/statuses/{statusId:guid}")]
    public async Task<IActionResult> DeleteStatus(Guid id, Guid statusId, [FromQuery] Guid moveTo, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(new StatusDeleteCommand(actor.Require(), id, statusId, moveTo), cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Новый порядок статусов — полный список Id. 400 — неполный или с чужими Id.</summary>
    [HttpPut("{id:guid}/statuses/order")]
    public async Task<IActionResult> ReorderStatuses(Guid id, ReorderStatusesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(new StatusReorderCommand(actor.Require(), id, request.StatusIds), cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    // ---- Workflow (docs/TZ_workflow_config.md §2) ----

    /// <summary>typeId — workflow типа (этап 3E): свой или проекта с inherited = true; чужой тип — 404.</summary>
    [HttpGet("{id:guid}/workflow")]
    public async Task<IActionResult> GetWorkflow(Guid id, [FromQuery] Guid? typeId, CancellationToken cancellationToken) =>
        await mediator.Send(new WorkflowGetQuery(actor.Require(), id, typeId), cancellationToken) is { } workflow ? Ok(workflow) : NotFound();

    /// <summary>Тип снова живёт по workflow проекта (этап 3E).</summary>
    [HttpDelete("{id:guid}/workflow")]
    public async Task<IActionResult> ResetTypeWorkflow(Guid id, [FromQuery] Guid typeId, CancellationToken cancellationToken) =>
        await mediator.Send(new WorkflowResetTypeCommand(actor.Require(), id, typeId), cancellationToken) is { } workflow ? Ok(workflow) : NotFound();

    /// <summary>Workflow целиком: режим и переходы. 400 — тупики в Restricted, чужой статус, повтор пары.</summary>
    [HttpPut("{id:guid}/workflow")]
    public async Task<IActionResult> SetWorkflow(Guid id, SetWorkflowRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var workflow = await mediator.Send(new WorkflowSetCommand(actor.Require(), id, request.Mode, request.Transitions, request.Layout, request.TaskTypeId), cancellationToken);
            return workflow is null ? NotFound() : Ok(workflow);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Окно финальной колонки канбана, дней (docs/TZ_task_views.md §1). 400 — вне 1…365.</summary>
    [HttpPut("{id:guid}/done-column-days")]
    public async Task<IActionResult> SetDoneColumnDays(Guid id, SetDoneColumnDaysRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(new BoardDoneColumnDaysSetCommand(actor.Require(), id, request.Days), cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    // ---- Доступ к проекту (docs/TZ_project_access.md, этап 4A) ----

    /// <summary>Роль и права текущего пользователя во всех проектах — клиент прячет по ним кнопки.</summary>
    [HttpGet("my-access")]
    public async Task<IActionResult> GetMyAccess(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new BoardMyAccessQuery(actor.Require()), cancellationToken));

    [HttpGet("{id:guid}/my-access")]
    public async Task<IActionResult> GetMyAccess(Guid id, CancellationToken cancellationToken)
    {
        var access = await mediator.Send(new BoardMyAccessQuery(actor.Require(), id), cancellationToken);
        return access.Count == 0 ? NotFound() : Ok(access[0]);
    }

    /// <summary>Роль по умолчанию и прямые участники проекта.</summary>
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken cancellationToken)
    {
        var members = await mediator.Send(new BoardMembersQuery(actor.Require(), id), cancellationToken);
        return members is null ? NotFound() : Ok(members);
    }

    /// <summary>Добавить участника или сменить роль. 400 — человек не найден, деактивирован или роль неизвестна.</summary>
    [HttpPut("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> SetMember(Guid id, Guid userId, SetBoardMemberRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Role))
            return BadRequest(new { Message = $"Unknown project role {request.Role}." });

        return await MemberResult(() => mediator.Send(
            new BoardMemberSetCommand(actor.Require(), id, userId, request.Role.ToDomainRole()), cancellationToken));
    }

    /// <summary>Убрать участие: роль вернётся к роли по умолчанию. 404 — человек не участник.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken) =>
        MemberResult(() => mediator.Send(new BoardMemberRemoveCommand(actor.Require(), id, userId), cancellationToken));

    /// <summary>Роль группы в проекте (этап 4C): каждый её участник получает эту роль. 400 — группы нет.</summary>
    [HttpPut("{id:guid}/groups/{groupId:guid}")]
    public async Task<IActionResult> SetGroup(Guid id, Guid groupId, SetBoardMemberRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Role))
            return BadRequest(new { Message = $"Unknown project role {request.Role}." });

        return await MemberResult(() => mediator.Send(
            new Flow.Application.Features.Groups.BoardGroupSetCommand(actor.Require(), id, groupId, request.Role.ToDomainRole()), cancellationToken));
    }

    [HttpDelete("{id:guid}/groups/{groupId:guid}")]
    public Task<IActionResult> RemoveGroup(Guid id, Guid groupId, CancellationToken cancellationToken) =>
        MemberResult(() => mediator.Send(new Flow.Application.Features.Groups.BoardGroupRemoveCommand(actor.Require(), id, groupId), cancellationToken));

    /// <summary>Open или Private (только участники и глобальные Admin/Owner). Права — ManageMembers.</summary>
    [HttpPut("{id:guid}/visibility")]
    public async Task<IActionResult> SetVisibility(Guid id, SetVisibilityRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Visibility))
            return BadRequest(new { Message = $"Unknown visibility {request.Visibility}." });

        var response = await mediator.Send(
            new BoardVisibilitySetCommand(actor.Require(), id, request.Visibility.ToDomainVisibility()), cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>Потолок роли без участия: Viewer, Member, Developer или null. Admin и неизвестное — 400.</summary>
    [HttpPut("{id:guid}/default-role")]
    public async Task<IActionResult> SetDefaultRole(Guid id, SetDefaultRoleRequest request, CancellationToken cancellationToken)
    {
        if (request.Role is { } role && !Enum.IsDefined(role))
            return BadRequest(new { Message = $"Unknown project role {role}." });

        try
        {
            var response = await mediator.Send(
                new BoardDefaultRoleSetCommand(actor.Require(), id, request.Role?.ToDomainRole()), cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>InvalidOperationException — последний администратор приватного проекта (400).</summary>
    private async Task<IActionResult> MemberResult(Func<Task<BoardMemberResult>> send)
    {
        BoardMemberResult result;
        try
        {
            result = await send();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }

        if (result.IsNotFound)
            return NotFound();

        return result.ValidationError is { } error ? BadRequest(new { Message = error }) : Ok(result.Response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBoard(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new BoardDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
