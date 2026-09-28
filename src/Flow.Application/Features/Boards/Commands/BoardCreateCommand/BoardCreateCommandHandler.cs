using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Templates;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Бросает ArgumentException, если Name/Key не проходят валидацию (см. Board.Create).</summary>
internal sealed class BoardCreateCommandHandler(
    IBoardRepository boards,
    IBoardTemplateRepository templates,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardCreateCommand, BoardCreateResult>
{
    public async Task<BoardCreateResult> Handle(BoardCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanCreateBoard(actor);

        var blueprint = request.TemplateId is { } templateId
            ? await BoardTemplates.ResolveAsync(templates, templateId, cancellationToken) ?? throw new ArgumentException("Шаблон проекта не найден.", nameof(request.TemplateId))
            : null;

        // Board.Create нормализует ключ (trim + upper), поэтому проверка уникальности идёт по board.Key,
        // а не по сырому request.Key: "flw" и "FLW" — одна и та же доска.
        var board = blueprint is null ? Board.Create(request.Name, request.Key) : Board.Create(request.Name, request.Key, blueprint);

        if (await boards.ExistsByKeyAsync(board.Key, cancellationToken))
            return BoardCreateResult.KeyTaken(board.Key);

        boards.Add(board);
        searchIndex.Enqueue(SearchSourceType.Board, board.Id, board.Id, SearchIndexOperation.Upsert);

        // Задачи-образцы шаблона — в начальный статус, по порядку; ранги подряд: соседей у нового проекта нет.
        var samples = blueprint?.SampleTasks ?? [];
        if (samples.Count > 0)
        {
            var typeIds = board.MapBlueprintTypes(blueprint!);
            var ranks = FractionalIndex.Sequence(null, samples.Count);
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var task = board.CreateTask(sample.Title, sample.Description, createdById: actor.Id,
                    typeId: sample.TypeKey is { } key && typeIds.TryGetValue(key, out var typeId) ? typeId : null, rank: ranks[i]);
                tasks.Add(task);
                activities.Add(TaskActivity.Created(task.Id, actor.Id));
                searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BoardCreateResult.Success(board.ToResponse(taskCount: samples.Count));
    }
}
