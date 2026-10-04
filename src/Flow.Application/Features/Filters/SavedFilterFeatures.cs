using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Filters;
using MediatR;

namespace Flow.Application.Features.Filters;

// Сохранённые фильтры (docs/TZ_task_views.md §7). Чужой личный фильтр для остальных не существует (null/false → 404),
// общий видят все. Править — автор, удалить — автор или Admin+ для общего. Запрос при сохранении проверяется
// биндингом FQL от имени автора: фильтр с опечаткой не должен сохраниться и молча отдавать пустоту.

public sealed record SavedFilterListQuery(Guid ActorId) : IRequest<IReadOnlyList<SavedFilterResponse>>;

public sealed record SavedFilterGetQuery(Guid ActorId, Guid FilterId) : IRequest<SavedFilterResponse?>;

/// <summary>
/// Обработчик - <see cref="SavedFilterHandlers"/>
/// </summary>
public sealed record SavedFilterCreateCommand(Guid ActorId, string Name, string Query, bool Shared) : IRequest<SavedFilterResponse>;

/// <summary>
/// Обработчик - <see cref="SavedFilterHandlers"/>
/// </summary>
public sealed record SavedFilterUpdateCommand(Guid ActorId, Guid FilterId, string? Name, string? Query, bool? Shared) : IRequest<SavedFilterResponse?>;

/// <summary>
/// Обработчик - <see cref="SavedFilterHandlers"/>
/// </summary>
public sealed record SavedFilterDeleteCommand(Guid ActorId, Guid FilterId) : IRequest<bool>;

/// <summary>
/// Звезда: фильтр в избранном сайдбара. Повтор — no-op.
/// Обработчик - <see cref="SavedFilterHandlers"/>
/// </summary>
public sealed record SavedFilterStarCommand(Guid ActorId, Guid FilterId, bool Starred) : IRequest<SavedFilterResponse?>;

internal static class SavedFilterMapping
{
    public static SavedFilterResponse ToResponse(this SavedFilter filter, Guid actorId, bool starred) => new(
        filter.Id, filter.Name, filter.Query, filter.View, filter.Visibility == SavedFilterVisibility.Shared,
        filter.OwnerId, filter.OwnerId == actorId, starred, filter.CreatedAt, filter.UpdatedAt);
}

internal sealed class SavedFilterHandlers(
    ISavedFilterRepository filters,
    IBoardRepository boards,
    IUserRepository users,
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones, IGroupRepository groupDirectory,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<SavedFilterListQuery, IReadOnlyList<SavedFilterResponse>>,
    IRequestHandler<SavedFilterGetQuery, SavedFilterResponse?>,
    IRequestHandler<SavedFilterCreateCommand, SavedFilterResponse>,
    IRequestHandler<SavedFilterUpdateCommand, SavedFilterResponse?>,
    IRequestHandler<SavedFilterDeleteCommand, bool>,
    IRequestHandler<SavedFilterStarCommand, SavedFilterResponse?>
{
    public async Task<IReadOnlyList<SavedFilterResponse>> Handle(SavedFilterListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var starred = await filters.GetStarredIdsAsync(actor.Id, cancellationToken);
        return (await filters.GetVisibleAsync(actor.Id, cancellationToken)).Select(f => f.ToResponse(actor.Id, starred.Contains(f.Id))).ToList();
    }

    public async Task<SavedFilterResponse?> Handle(SavedFilterGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var filter = await VisibleAsync(actor, request.FilterId, cancellationToken);
        return filter is null ? null : filter.ToResponse(actor.Id, await IsStarredAsync(filter, actor, cancellationToken));
    }

    public async Task<SavedFilterResponse> Handle(SavedFilterCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var filter = SavedFilter.Create(actor.Id, request.Name, request.Query, Visibility(request.Shared));
        await ValidateAsync(actor, filter.Query, cancellationToken);

        filters.Add(filter);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return filter.ToResponse(actor.Id, false);
    }

    public async Task<SavedFilterResponse?> Handle(SavedFilterUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var filter = await VisibleAsync(actor, request.FilterId, cancellationToken);
        if (filter is null)
            return null;

        permissions.EnsureCanEditSavedFilter(actor, filter);
        filter.Update(request.Name, request.Query, request.Shared is { } shared ? Visibility(shared) : null);
        if (request.Query is not null)
            await ValidateAsync(actor, filter.Query, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return filter.ToResponse(actor.Id, await IsStarredAsync(filter, actor, cancellationToken));
    }

    public async Task<bool> Handle(SavedFilterDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var filter = await VisibleAsync(actor, request.FilterId, cancellationToken);
        if (filter is null)
            return false;

        permissions.EnsureCanDeleteSavedFilter(actor, filter);
        filters.Remove(filter);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SavedFilterResponse?> Handle(SavedFilterStarCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var filter = await VisibleAsync(actor, request.FilterId, cancellationToken);
        if (filter is null)
            return null;

        var star = await filters.GetStarAsync(filter.Id, actor.Id, cancellationToken);
        if (request.Starred && star is null)
            filters.AddStar(new SavedFilterStar(filter.Id, actor.Id));
        else if (!request.Starred && star is not null)
            filters.RemoveStar(star);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return filter.ToResponse(actor.Id, request.Starred);
    }

    private async Task<SavedFilter?> VisibleAsync(User actor, Guid id, CancellationToken cancellationToken)
    {
        var filter = await filters.GetByIdAsync(id, cancellationToken);
        return filter is not null && filter.IsVisibleTo(actor.Id) ? filter : null;
    }

    private async Task<bool> IsStarredAsync(SavedFilter filter, User actor, CancellationToken cancellationToken) =>
        await filters.GetStarAsync(filter.Id, actor.Id, cancellationToken) is not null;

    private async Task ValidateAsync(User actor, string query, CancellationToken cancellationToken)
    {
        var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones, groupDirectory);
        await FqlBinder.BindAsync(query, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }

    private static SavedFilterVisibility Visibility(bool shared) => shared ? SavedFilterVisibility.Shared : SavedFilterVisibility.Private;
}
