using System.Text;
using Flow.Application.Features.Scm;
using Flow.Domain.Entities;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Чистые функции интеграции с Git (docs/TZ_scm_integration.md §2–3): детектор кодов, подписи каждого провайдера,
/// разбор payload'ов в нормализованное событие. Payload'ы — по форме документации хостингов.
/// </summary>
public class ScmParsingTests
{
    private static Func<string, string?> Headers(params (string Name, string Value)[] headers) =>
        name => headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    [Theory]
    [InlineData("Fix WEB-12 login", new[] { "WEB-12" })]
    [InlineData("WEB-12a and XWEB-12b, web-12", new string[0])]
    [InlineData("see https://flow/tasks/WEB-7 and (API-001)", new[] { "WEB-7", "API-1" })]
    [InlineData("WEB-12: WEB-12 again, OPS-3", new[] { "WEB-12", "OPS-3" })]
    public void Detector_Finds_Codes_On_Word_Boundaries(string text, string[] codes) =>
        Assert.Equal(codes, TaskCodeDetector.Find(text));

    [Fact]
    public void Branch_Names_Ignore_Case_And_Separators()
    {
        Assert.Equal(["WEB-12"], TaskCodeDetector.FindInBranch("feature/web-12_login"));
        Assert.Equal(["WEB-12", "API-3"], TaskCodeDetector.FindInBranch("WEB-12-and-api-3"));
    }

    [Fact]
    public void Signatures_Of_Every_Provider()
    {
        var body = Encoding.UTF8.GetBytes("{\"zen\":\"ok\"}");
        const string secret = "s3cr3t";
        var hex = ScmSignatures.Sign(body, secret);

        Assert.True(ScmSignatures.Verify(ScmProvider.GitHub, Headers(("X-Hub-Signature-256", "sha256=" + hex)), body, secret));
        Assert.False(ScmSignatures.Verify(ScmProvider.GitHub, Headers(("X-Hub-Signature-256", "sha256=" + hex)), body, "other"));
        Assert.False(ScmSignatures.Verify(ScmProvider.GitHub, Headers(("X-Hub-Signature-256", hex)), body, secret));
        Assert.True(ScmSignatures.Verify(ScmProvider.GitLab, Headers(("X-Gitlab-Token", secret)), body, secret));
        Assert.False(ScmSignatures.Verify(ScmProvider.GitLab, Headers(), body, secret));
        Assert.True(ScmSignatures.Verify(ScmProvider.Gitea, Headers(("X-Gitea-Signature", hex)), body, secret));
        Assert.True(ScmSignatures.Verify(ScmProvider.Forgejo, Headers(("X-Forgejo-Signature", hex)), body, secret));
        Assert.False(ScmSignatures.Verify(ScmProvider.Forgejo, Headers(("X-Forgejo-Signature", "zz")), body, secret));
    }

    [Fact]
    public void GitHub_Push_Pull_Request_And_Branches()
    {
        var push = ScmPayloadParser.Parse(ScmProvider.GitHub, "push", Encoding.UTF8.GetBytes("""
            {"ref":"refs/heads/web-12-login","after":"abc","deleted":false,
             "commits":[{"id":"abc","message":"WEB-12 add form\n\nbody","url":"https://github.com/a/b/commit/abc",
                         "timestamp":"2026-09-20T10:00:00+03:00","author":{"email":"dev@example.com","username":"octocat"}}]}
            """))!;
        Assert.Equal((ScmEventKind.Push, "web-12-login", 1), (push.Kind, push.Branch, push.TotalCommits));
        Assert.Equal(("abc", "dev@example.com", "octocat"), (push.Commits![0].Sha, push.Commits[0].AuthorEmail, push.Commits[0].AuthorLogin));
        Assert.Equal(new DateTime(2026, 9, 20, 7, 0, 0, DateTimeKind.Utc), push.Commits[0].Timestamp);

        var pr = ScmPayloadParser.Parse(ScmProvider.GitHub, "pull_request", Encoding.UTF8.GetBytes("""
            {"action":"closed","pull_request":{"number":42,"title":"WEB-12 Login","body":null,"state":"closed","merged":true,
             "html_url":"https://github.com/a/b/pull/42","user":{"login":"octocat"},"head":{"ref":"web-12-login"},"base":{"ref":"main"},
             "updated_at":"2026-09-21T10:00:00Z"}}
            """))!.PullRequest!;
        Assert.Equal(("42", ScmLinkState.Merged, "web-12-login", "main"), (pr.Number, pr.State, pr.SourceBranch, pr.TargetBranch));

        Assert.Equal(ScmEventKind.BranchCreated, ScmPayloadParser.Parse(ScmProvider.GitHub, "create", Encoding.UTF8.GetBytes("""{"ref":"web-1","ref_type":"branch"}"""))!.Kind);
        Assert.Null(ScmPayloadParser.Parse(ScmProvider.GitHub, "create", Encoding.UTF8.GetBytes("""{"ref":"v1","ref_type":"tag"}""")));
        Assert.Null(ScmPayloadParser.Parse(ScmProvider.GitHub, "issues", Encoding.UTF8.GetBytes("{}")));
        Assert.Equal(ScmEventKind.BranchDeleted, ScmPayloadParser.Parse(ScmProvider.GitHub, "push",
            Encoding.UTF8.GetBytes("""{"ref":"refs/heads/x","deleted":true,"after":"0000000000000000000000000000000000000000"}"""))!.Kind);
    }

    [Fact]
    public void GitLab_Push_Branch_And_Merge_Request()
    {
        var created = ScmPayloadParser.Parse(ScmProvider.GitLab, "Push Hook", Encoding.UTF8.GetBytes("""
            {"ref":"refs/heads/WEB-5","before":"0000000000000000000000000000000000000000","after":"abc","commits":[],"total_commits_count":0}
            """))!;
        Assert.Equal((ScmEventKind.BranchCreated, "WEB-5"), (created.Kind, created.Branch));

        var mr = ScmPayloadParser.Parse(ScmProvider.GitLab, "Merge Request Hook", Encoding.UTF8.GetBytes("""
            {"user":{"username":"dev"},"object_attributes":{"iid":7,"title":"Draft: WEB-5","description":"","state":"opened","draft":true,
             "url":"https://gitlab.example.com/a/b/-/merge_requests/7","source_branch":"WEB-5","target_branch":"main","updated_at":"2026-09-21 10:00:00 UTC"}}
            """))!.PullRequest!;
        Assert.Equal(("7", ScmLinkState.Draft, "dev"), (mr.Number, mr.State, mr.AuthorLogin));
        Assert.Equal(new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc), mr.UpdatedAt);
    }

    [Fact]
    public void Gitea_Pull_Request_And_Header_Names()
    {
        var pr = ScmPayloadParser.Parse(ScmProvider.Forgejo, "pull_request", Encoding.UTF8.GetBytes("""
            {"action":"opened","pull_request":{"number":3,"title":"OPS-1","state":"open","merged":false,"html_url":"https://git.example.com/a/b/pulls/3",
             "user":{"login":"dev"},"head":{"ref":"ops-1"},"base":{"ref":"main"}}}
            """))!.PullRequest!;
        Assert.Equal(ScmLinkState.Open, pr.State);

        Assert.Equal("push", ScmPayloadParser.EventName(ScmProvider.Forgejo, Headers(("X-Forgejo-Event", "push"))));
        Assert.Equal("push", ScmPayloadParser.EventName(ScmProvider.Gitea, Headers(("X-Gitea-Event", "push"))));
        Assert.Equal("d-1", ScmPayloadParser.DeliveryId(ScmProvider.GitHub, Headers(("X-GitHub-Delivery", "d-1"))));
    }
}
