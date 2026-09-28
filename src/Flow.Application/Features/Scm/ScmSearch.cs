using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;

namespace Flow.Application.Features.Scm;

/// <summary>
/// PR и коммиты в поисковом индексе (docs/TZ_scm_integration.md, этап 5E). Источник — связь ScmLink, проект — проект
/// задачи. Ветки не индексируются: их имя — код и название задачи, которые и так в индексе.
/// </summary>
public static class ScmSearch
{
    /// <summary>Что из связи идёт в текст чанка: поменялось — связь переиндексируется.</summary>
    public static (string Title, string? Source, string? Target) Snapshot(ScmLink link) => (link.Title, link.SourceBranch, link.TargetBranch);

    public static bool IsIndexed(ScmLinkKind kind) => kind != ScmLinkKind.Branch;

    /// <param name="priority">0 — живое событие, 1 — дозагрузка истории: сотни связей не должны обгонять правки людей.</param>
    public static void Upsert(this ISearchIndexQueue queue, ScmLink link, Guid boardId, int priority = 0)
    {
        if (IsIndexed(link.Kind))
            queue.Enqueue(SearchSourceType.Development, link.Id, boardId, SearchIndexOperation.Upsert, priority);
    }

    public static void Delete(this ISearchIndexQueue queue, ScmLink link, Guid boardId)
    {
        if (IsIndexed(link.Kind))
            queue.Enqueue(SearchSourceType.Development, link.Id, boardId, SearchIndexOperation.Delete);
    }
}
