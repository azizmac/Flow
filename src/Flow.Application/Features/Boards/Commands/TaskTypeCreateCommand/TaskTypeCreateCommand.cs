using Flow.Shared.Contracts.Boards;
using MediatR;
using TaskTypeKind = Flow.Domain.Entities.TaskTypeKind;

namespace Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;

/// <summary>
/// Добавить тип задачи в проект (docs/TZ_task_model.md §1). Права — как на управление проектами (Admin+).
/// Response = null, если проекта нет; занятое имя — InvalidOperationException (400). Возвращает проект целиком:
/// клиенту нужен обновлённый список типов вместе с флагом «по умолчанию», который мог переехать.
/// Обработчик - <see cref="TaskTypeCreateCommandHandler"/>
/// </summary>
public sealed record TaskTypeCreateCommand(Guid ActorId, Guid BoardId, string Name, TaskTypeKind Kind, bool IsDefault = false)
    : IRequest<BoardResponse?>;
