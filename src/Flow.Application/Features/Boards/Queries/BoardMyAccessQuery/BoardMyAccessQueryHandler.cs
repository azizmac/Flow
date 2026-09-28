using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;

internal sealed class BoardMyAccessQueryHandler(IBoardMemberRepository members, IPermissionSetRepository permissionSets, ActorResolver actors)
    : IRequestHandler<BoardMyAccessQuery, IReadOnlyList<ProjectAccessResponse>>
{
    public async Task<IReadOnlyList<ProjectAccessResponse>> Handle(BoardMyAccessQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        IReadOnlyList<BoardAccessData> data = request.BoardId is { } boardId
            ? await members.GetAccessDataAsync(boardId, actor.Id, cancellationToken) is { } one ? [one] : []
            : await members.GetAccessDataForUserAsync(actor.Id, cancellationToken);

        // Скрытые проекты в ответ не попадают: их права — «ничего», и сам факт существования не нужен клиенту.
        var sets = await ProjectAccess.SetsAsync(permissionSets, data, cancellationToken);
        return data
            .Select(d => ProjectRoles.Resolve(actor.Role, d, sets))
            .Where(a => a.CanView)
            .Select(a => a.ToResponse())
            .ToList();
    }
}
