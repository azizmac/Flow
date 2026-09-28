using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;

// Чек-лист задачи (docs/TZ_task_model.md §8). Права — как правка задачи (исполнитель-Member отмечает пункты своей
// задачи). Ответ — чек-лист целиком, по порядку; null — задачи нет (404). Неизвестный пункт, пустой или слишком
// длинный текст, больше 100 пунктов, неполный порядок — 400. Журнал ChecklistChanged пишется, только когда меняется
// прогресс «выполнено/всего»: правка текста и порядок его не трогают.

public sealed record TaskChecklistAddCommand(Guid ActorId, Guid TaskId, string Text) : IRequest<IReadOnlyList<TaskChecklistItemResponse>?>;

/// <summary>PATCH-семантика: null — не трогать.</summary>
public sealed record TaskChecklistUpdateCommand(Guid ActorId, Guid TaskId, Guid ItemId, string? Text = null, bool? IsDone = null)
    : IRequest<IReadOnlyList<TaskChecklistItemResponse>?>;

public sealed record TaskChecklistDeleteCommand(Guid ActorId, Guid TaskId, Guid ItemId) : IRequest<IReadOnlyList<TaskChecklistItemResponse>?>;

public sealed record TaskChecklistReorderCommand(Guid ActorId, Guid TaskId, IReadOnlyList<Guid> ItemIds) : IRequest<IReadOnlyList<TaskChecklistItemResponse>?>;
