using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Features.GitIntegration;

/// <summary>
/// Событие хостинга, нормализованное до полей, которые разбирает Flow (docs/TZ_git_integration.md §1): его JSON и
/// хранится в GitIntegrationJob.Payload — не мегабайтный push целиком. Разные провайдеры дают одну форму.
/// </summary>
public sealed record GitEvent(
    GitEventKind Kind,
    string? Branch = null,
    IReadOnlyList<GitCommit>? Commits = null,
    int TotalCommits = 0,
    GitPullRequest? PullRequest = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static GitEvent Deserialize(string json) => JsonSerializer.Deserialize<GitEvent>(json, Json)!;
}

public enum GitEventKind { Push, PullRequest, BranchCreated, BranchDeleted }

public sealed record GitCommit(string Sha, string Message, string Url, string? AuthorEmail, string? AuthorLogin, DateTime Timestamp);

public sealed record GitPullRequest(
    string Number, string Title, string? Body, GitDevelopmentLinkState State, string Url, string? AuthorLogin,
    string? SourceBranch, string? TargetBranch, DateTime UpdatedAt);
