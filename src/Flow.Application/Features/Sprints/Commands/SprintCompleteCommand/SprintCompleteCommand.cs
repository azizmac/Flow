using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintCompleteCommand;

/// <summary>
/// Завершить активный спринт. Незакрытые задачи (статус не финальный) уезжают в спринт MoveOpenTo того же проекта
/// или в бэклог (null) — у каждой SprintChanged в журнале; закрытые остаются в спринте. Права — ManageSprints.
/// Обработчик - <see cref="SprintCompleteCommandHandler"/>
/// </summary>
public sealed record SprintCompleteCommand(Guid ActorId, Guid SprintId, Guid? MoveOpenTo = null) : IRequest<SprintResponse?>;
