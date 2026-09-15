using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Search.Commands.ReindexCommand;

/// <summary>
/// Только ставит в очередь — векторы считает фоновый воркер. Поэтому ответ 202, а не 200: к моменту
/// ответа индекс ещё не перестроен. Бросает InvalidOperationException (→ 400), если поиск выключен:
/// молча наполнять очередь, которую никто не разберёт, хуже, чем сказать об этом.
/// </summary>
internal sealed class ReindexCommandHandler(
    SearchOptions options,
    ISearchIndexStore store,
    ActorResolver actors,
    IPermissionService permissions)
    : IRequestHandler<ReindexCommand, int>
{
    public async Task<int> Handle(ReindexCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanReindex(await actors.ResolveAsync(request.ActorId, cancellationToken));

        if (!options.Enabled)
            throw new InvalidOperationException("Поиск выключен (Search:Enabled=false) — переиндексировать нечего.");

        IReadOnlyCollection<SearchSourceType> types =
            request.Types is { Count: > 0 } requested ? requested : DefaultTypes;

        return await store.EnqueueAllAsync(types, request.BoardId, cancellationToken);
    }

    /// <summary>Attachment сюда не входит: тип заведён заранее, источников у него пока нет.</summary>
    private static readonly SearchSourceType[] DefaultTypes =
    [
        SearchSourceType.Task,
        SearchSourceType.Comment,
        SearchSourceType.Board,
        SearchSourceType.User
    ];
}
