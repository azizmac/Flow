using System.Text.Json;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

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
    public static string? EventName(GitProvider provider, Func<string, string?> header) => provider switch
    {
        GitProvider.GitHub => header("X-GitHub-Event"),
        GitProvider.GitLab => header("X-Gitlab-Event"),
        _ => header("X-Forgejo-Event") ?? header("X-Gitea-Event")
    };

    /// <summary>Id доставки из заголовков: повтор с тем же Id не обрабатывается дважды.</summary>
    public static string? DeliveryId(GitProvider provider, Func<string, string?> header) => provider switch
    {
        GitProvider.GitHub => header("X-GitHub-Delivery"),
        GitProvider.GitLab => header("X-Gitlab-Event-UUID") ?? header("X-Gitlab-Webhook-UUID"),
        _ => header("X-Forgejo-Delivery") ?? header("X-Gitea-Delivery")
    };

    public static ScmEvent? Parse(GitProvider provider, string eventName, ReadOnlySpan<byte> body)
    {
        using var document = JsonDocument.Parse(body.ToArray());
        var root = document.RootElement;
        return provider == GitProvider.GitLab ? ParseGitLab(eventName, root) : ParseGitHubLike(provider, eventName, root);
    }

    private static ScmEvent? ParseGitHubLike(GitProvider provider, string eventName, JsonElement root)
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
                return pr.ValueKind != JsonValueKind.Object ? null : new ScmEvent(ScmEventKind.PullRequest, PullRequest: GitHubPullRequest(pr));
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
                return new ScmEvent(ScmEventKind.PullRequest, PullRequest: new ScmPullRequest(
                    Num(mr, "iid"), Str(mr, "title") ?? "", Str(mr, "description"), GitLabState(mr), Str(mr, "url") ?? "",
                    Str(Obj(root, "user"), "username"), Str(mr, "source_branch"), Str(mr, "target_branch"),
                    Date(Str(mr, "updated_at") ?? Str(mr, "created_at"))));
            }
            default:
                return null;
        }
    }

    /// <summary>PR GitHub/Gitea — одна форма у вебхука и у REST. В списке REST GitHub нет поля merged — смотрим merged_at.</summary>
    private static ScmPullRequest GitHubPullRequest(JsonElement pr)
    {
        var state = Bool(pr, "merged") || Str(pr, "merged_at") is not null ? GitDevelopmentLinkState.Merged
            : Str(pr, "state") == "closed" ? GitDevelopmentLinkState.Closed
            : Bool(pr, "draft") ? GitDevelopmentLinkState.Draft
            : GitDevelopmentLinkState.Open;
        return new ScmPullRequest(
            Num(pr, "number"), Str(pr, "title") ?? "", Str(pr, "body"), state, Str(pr, "html_url") ?? "",
            Str(Obj(pr, "user"), "login"), Str(Obj(pr, "head"), "ref"), Str(Obj(pr, "base"), "ref"),
            Date(Str(pr, "updated_at") ?? Str(pr, "created_at")));
    }

    private static GitDevelopmentLinkState GitLabState(JsonElement mr) => Str(mr, "state") switch
    {
        "merged" => GitDevelopmentLinkState.Merged,
        "closed" or "locked" => GitDevelopmentLinkState.Closed,
        _ => Bool(mr, "draft") || Bool(mr, "work_in_progress") ? GitDevelopmentLinkState.Draft : GitDevelopmentLinkState.Open
    };

    /// <summary>Список PR/MR из REST API (дозагрузка истории, этап 5B). У GitLab форма MR в REST своя, не как в вебхуке.</summary>
    public static IReadOnlyList<ScmPullRequest> ParsePullRequests(GitProvider provider, JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return [];
        return provider == GitProvider.GitLab
            ? array.EnumerateArray().Select(mr => new ScmPullRequest(
                Num(mr, "iid"), Str(mr, "title") ?? "", Str(mr, "description"), GitLabState(mr), Str(mr, "web_url") ?? "",
                Str(Obj(mr, "author"), "username"), Str(mr, "source_branch"), Str(mr, "target_branch"),
                Date(Str(mr, "updated_at") ?? Str(mr, "created_at")))).ToList()
            : array.EnumerateArray().Select(GitHubPullRequest).ToList();
    }

    /// <summary>Список коммитов из REST API. GitHub и Gitea: sha + commit.{message, author}; GitLab: плоский объект.</summary>
    public static IReadOnlyList<ScmCommit> ParseCommits(GitProvider provider, JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return [];
        return provider == GitProvider.GitLab
            ? array.EnumerateArray().Select(c => new ScmCommit(
                Str(c, "id") ?? "", Str(c, "message") ?? "", Str(c, "web_url") ?? "", Str(c, "author_email"), null,
                Date(Str(c, "committed_date") ?? Str(c, "authored_date")))).ToList()
            : array.EnumerateArray().Select(c => new ScmCommit(
                Str(c, "sha") ?? "", Str(Obj(c, "commit"), "message") ?? "", Str(c, "html_url") ?? "",
                Str(Obj(Obj(c, "commit"), "author"), "email"), Str(Obj(c, "author"), "login"),
                Date(Str(Obj(Obj(c, "commit"), "author"), "date")))).ToList();
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
