using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;

/// <summary>
/// Переименовать, сделать типом по умолчанию, архивировать или вернуть тип. PATCH-семантика: null — не трогать;
/// IsDefault = false не поддерживается (флаг снимается, только когда его получает другой тип) — 400.
/// Права — Admin+. Response = null, если проекта нет; тип из другого проекта, архивация типа по умолчанию,
/// занятое имя — InvalidOperationException (400).
/// Обработчик - <see cref="TaskTypeUpdateCommandHandler"/>
/// </summary>
public sealed record TaskTypeUpdateCommand(Guid ActorId, Guid BoardId, Guid TypeId, string? Name = null, bool? IsDefault = null, bool? IsArchived = null)
    : IRequest<BoardResponse?>;
