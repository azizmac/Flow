using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskLinkDeleteCommand;

/// <summary>
/// Убрать связь. false — связи нет или actor не видит ни одной из задач (404). Права — правка любой из двух задач:
/// связь принадлежит обеим. Журнал LinkRemoved — у обеих.
/// Обработчик - <see cref="TaskLinkDeleteCommandHandler"/>
/// </summary>
public sealed record TaskLinkDeleteCommand(Guid ActorId, Guid LinkId) : IRequest<bool>;
