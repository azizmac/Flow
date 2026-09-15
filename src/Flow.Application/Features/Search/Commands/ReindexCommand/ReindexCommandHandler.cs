using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Commands.ReindexCommand;

internal sealed class ReindexCommandHandler(
    ISearchIndexRepository index,
    SearchOptions options,
    ActorResolver actors,
    IPermissionService permissions)
    : IRequestHandler<ReindexCommand, int>
{
    public async Task<int> Handle(ReindexCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanReindex(await actors.ResolveAsync(request.ActorId, cancellationToken));

        if (!options.Enabled)
            throw new InvalidOperationException("Поиск выключен (Search:Enabled=false) — переиндексировать нечего.");

        var types = request.Types ?? [];
        foreach (var type in types)
        {
            if (type is SearchSourceType.Attachment)
                throw new ArgumentException("Вложения пока не индексируются.", nameof(request.Types));
        }

        // Постановка в очередь — не сама индексация: чанки построит воркер, эндпоинт отвечает 202.
        return await index.EnqueueAllAsync(types, request.BoardId, cancellationToken);
    }
}
