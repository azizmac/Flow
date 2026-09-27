using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;

internal sealed class BoardMyAccessQueryHandler(IBoardMemberRepository members, ActorResolver actors)
    : IRequestHandler<BoardMyAccessQuery, IReadOnlyList<ProjectAccessResponse>>
{
    public async Task<IReadOnlyList<ProjectAccessResponse>> Handle(BoardMyAccessQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        IReadOnlyList<BoardAccessData> data = request.BoardId is { } boardId
            ? await members.GetAccessDataAsync(boardId, actor.Id, cancellationToken) is { } one ? [one] : []
            : await members.GetAccessDataForUserAsync(actor.Id, cancellationToken);

        // Скрытые проекты в ответ не попадают: их права — «ничего», и сам факт существования не нужен клиенту.
        return data
            .Select(d => ProjectRoles.Access(d.BoardId, ProjectRoles.Effective(actor.Role, d.Visibility, d.DefaultRole, d.MemberRole)))
            .Where(a => a.CanView)
            .Select(a => a.ToResponse())
            .ToList();
    }
}
