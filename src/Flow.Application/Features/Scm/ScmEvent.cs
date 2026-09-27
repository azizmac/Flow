using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Scm;

/// <summary>
/// Событие хостинга, нормализованное до полей, которые разбирает Flow (docs/TZ_scm_integration.md §1): его JSON и
/// хранится в ScmDelivery.Payload — не мегабайтный push целиком. Разные провайдеры дают одну форму.
/// </summary>
public sealed record ScmEvent(
    ScmEventKind Kind,
    string? Branch = null,
    IReadOnlyList<ScmCommit>? Commits = null,
    int TotalCommits = 0,
    ScmPullRequest? PullRequest = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static ScmEvent Deserialize(string json) => JsonSerializer.Deserialize<ScmEvent>(json, Json)!;
}

public enum ScmEventKind { Push, PullRequest, BranchCreated, BranchDeleted }

public sealed record ScmCommit(string Sha, string Message, string Url, string? AuthorEmail, string? AuthorLogin, DateTime Timestamp);

public sealed record ScmPullRequest(
    string Number, string Title, string? Body, ScmLinkState State, string Url, string? AuthorLogin,
    string? SourceBranch, string? TargetBranch, DateTime UpdatedAt);
