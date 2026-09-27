using System.Text.Json;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Scm;

/// <summary>
/// Разбор вебхуков GitHub, GitLab и Gitea/Forgejo (docs/TZ_scm_integration.md §2–3) в <see cref="ScmEvent"/>.
/// null — событие Flow не нужно (метки, issue, звёзды…): доставка пишется как Ignored, чтобы вебхук с лишними
/// галочками не копил ошибки. Бросает JsonException на битом теле — это уже 400, а не повтор.
/// </summary>
public static class ScmPayloadParser
{
    /// <summary>Сколько коммитов push'а разбирать; остальные — ссылкой «ещё N».</summary>
    public const int MaxCommits = 100;

    private const string ZeroSha = "0000000000000000000000000000000000000000";

    /// <summary>Имя события из заголовков провайдера.</summary>
    public static string? EventName(ScmProvider provider, Func<string, string?> header) => provider switch
    {
        ScmProvider.GitHub => header("X-GitHub-Event"),
        ScmProvider.GitLab => header("X-Gitlab-Event"),
        _ => header("X-Forgejo-Event") ?? header("X-Gitea-Event")
    };

    /// <summary>Id доставки из заголовков: повтор с тем же Id не обрабатывается дважды.</summary>
    public static string? DeliveryId(ScmProvider provider, Func<string, string?> header) => provider switch
    {
        ScmProvider.GitHub => header("X-GitHub-Delivery"),
        ScmProvider.GitLab => header("X-Gitlab-Event-UUID") ?? header("X-Gitlab-Webhook-UUID"),
        _ => header("X-Forgejo-Delivery") ?? header("X-Gitea-Delivery")
    };

    public static ScmEvent? Parse(ScmProvider provider, string eventName, ReadOnlySpan<byte> body)
    {
        using var document = JsonDocument.Parse(body.ToArray());
        var root = document.RootElement;
        return provider == ScmProvider.GitLab ? ParseGitLab(eventName, root) : ParseGitHubLike(provider, eventName, root);
    }

    private static ScmEvent? ParseGitHubLike(ScmProvider provider, string eventName, JsonElement root)
    {
        switch (eventName)
        {
            case "push":
            {
                var branch = BranchOf(Str(root, "ref"));
                if (branch is null)
                    return null;
                if (Bool(root, "deleted") || Str(root, "after") == ZeroSha)
                    return new ScmEvent(ScmEventKind.BranchDeleted, branch);

                var commits = Array(root, "commits").Select(c => new ScmCommit(
                    Str(c, "id") ?? "", Str(c, "message") ?? "", Str(c, "url") ?? "",
                    Str(Obj(c, "author"), "email"), Str(Obj(c, "author"), "username"),
                    Date(Str(c, "timestamp")))).ToList();
                return new ScmEvent(ScmEventKind.Push, branch, commits.Take(MaxCommits).ToList(), commits.Count);
            }
            case "create" or "delete" when Str(root, "ref_type") == "branch":
                return new ScmEvent(eventName == "create" ? ScmEventKind.BranchCreated : ScmEventKind.BranchDeleted, Str(root, "ref"));
            case "pull_request":
            {
                var pr = Obj(root, "pull_request");
                if (pr.ValueKind != JsonValueKind.Object)
                    return null;
                var merged = Bool(pr, "merged");
                var state = merged ? ScmLinkState.Merged
                    : Str(pr, "state") == "closed" ? ScmLinkState.Closed
                    : Bool(pr, "draft") ? ScmLinkState.Draft
                    : ScmLinkState.Open;
                return new ScmEvent(ScmEventKind.PullRequest, PullRequest: new ScmPullRequest(
                    Num(pr, "number"), Str(pr, "title") ?? "", Str(pr, "body"), state, Str(pr, "html_url") ?? "",
                    Str(Obj(pr, "user"), "login"), Str(Obj(pr, "head"), "ref"), Str(Obj(pr, "base"), "ref"),
                    Date(Str(pr, "updated_at") ?? Str(pr, "created_at"))));
            }
            default:
                return null;
        }
    }

    private static ScmEvent? ParseGitLab(string eventName, JsonElement root)
    {
        switch (eventName)
        {
            case "Push Hook":
            {
                var branch = BranchOf(Str(root, "ref"));
                if (branch is null)
                    return null;
                if (Str(root, "after") == ZeroSha)
                    return new ScmEvent(ScmEventKind.BranchDeleted, branch);

                var commits = Array(root, "commits").Select(c => new ScmCommit(
                    Str(c, "id") ?? "", Str(c, "message") ?? "", Str(c, "url") ?? "",
                    Str(Obj(c, "author"), "email"), null, Date(Str(c, "timestamp")))).ToList();
                var total = root.TryGetProperty("total_commits_count", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : commits.Count;
                // У GitLab создание ветки — тот же Push Hook с нулевым before: отдельного события нет.
                if (Str(root, "before") == ZeroSha && commits.Count == 0)
                    return new ScmEvent(ScmEventKind.BranchCreated, branch);
                return new ScmEvent(ScmEventKind.Push, branch, commits.Take(MaxCommits).ToList(), total);
            }
            case "Merge Request Hook":
            {
                var mr = Obj(root, "object_attributes");
                if (mr.ValueKind != JsonValueKind.Object)
                    return null;
                var state = Str(mr, "state") switch
                {
                    "merged" => ScmLinkState.Merged,
                    "closed" or "locked" => ScmLinkState.Closed,
                    _ => Bool(mr, "draft") || Bool(mr, "work_in_progress") ? ScmLinkState.Draft : ScmLinkState.Open
                };
                return new ScmEvent(ScmEventKind.PullRequest, PullRequest: new ScmPullRequest(
                    Num(mr, "iid"), Str(mr, "title") ?? "", Str(mr, "description"), state, Str(mr, "url") ?? "",
                    Str(Obj(root, "user"), "username"), Str(mr, "source_branch"), Str(mr, "target_branch"),
                    Date(Str(mr, "updated_at") ?? Str(mr, "created_at"))));
            }
            default:
                return null;
        }
    }

    private static string? BranchOf(string? gitRef) =>
        gitRef is not null && gitRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? gitRef["refs/heads/".Length..] : null;

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        Obj(e, name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    private static string? Str(JsonElement e, string name) =>
        Obj(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static string Num(JsonElement e, string name) => Obj(e, name) switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        { ValueKind: JsonValueKind.String } s => s.GetString() ?? "",
        _ => ""
    };

    private static bool Bool(JsonElement e, string name) => Obj(e, name).ValueKind == JsonValueKind.True;

    /// <summary>ISO-дата хостинга; у GitLab встречается «2026-09-01 10:00:00 UTC» — тоже понимаем.</summary>
    private static DateTime Date(string? value)
    {
        if (value is null)
            return DateTime.UtcNow;
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.UtcDateTime;
        return DateTimeOffset.TryParse(value.Replace(" UTC", "Z"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out parsed) ? parsed.UtcDateTime : DateTime.UtcNow;
    }
}
