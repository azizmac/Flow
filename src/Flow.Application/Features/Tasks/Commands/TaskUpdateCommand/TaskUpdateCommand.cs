using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>
/// PATCH-семантика: заполненные поля меняются, null — не трогать. TypeId — тип того же проекта (иначе 400),
/// архивный тип не принимается, если задача уже не на нём.
/// Обработчик - <see cref="TaskUpdateCommandHandler"/>
/// </summary>
public sealed record TaskUpdateCommand(
    Guid ActorId,
    Guid TaskId,
    string? Title,
    string? Description,
    Guid? StatusId,
    Guid? TypeId = null,
    TaskPriority? Priority = null)
    : IRequest<TaskUpdateResult>;
