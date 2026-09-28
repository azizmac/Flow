using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Инварианты интеграции с Git этапа 5B: подключение GitHub App, пауза и ручной повтор доставки, задание дозагрузки.</summary>
public class ScmTests
{
    [Fact]
    public void GitHub_App_Needs_Ids_And_Is_GitHub_Only()
    {
        var owner = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ScmConnection.Create(ScmProvider.GitLab, "GitLab", "https://gl.example.com", "p", owner, ScmAuthKind.GitHubApp, 1, 2));
        Assert.Throws<ArgumentException>(() => ScmConnection.Create(ScmProvider.GitHub, "App", null, "p", owner, ScmAuthKind.GitHubApp, 1, null));

        var app = ScmConnection.Create(ScmProvider.GitHub, "App", null, "p", owner, ScmAuthKind.GitHubApp, 1, 2);
        app.MarkChecked("flow[bot]", null, DateTime.UtcNow);
        app.SetApp(1, 3);
        Assert.Equal((3L, (string?)null), (app.InstallationId, app.CheckedLogin));

        var token = ScmConnection.Create(ScmProvider.GitHub, "Token", null, "p", owner);
        Assert.Throws<InvalidOperationException>(() => token.SetApp(1, 2));
    }

    [Fact]
    public void Postpone_Keeps_Attempts_And_Retry_Restarts_Failed()
    {
        var delivery = ScmDelivery.CreateBackfill(Guid.NewGuid());
        Assert.True(delivery.IsBackfill);
        Assert.Equal(ScmDeliveryStatus.Pending, delivery.Status);

        var until = DateTime.UtcNow.AddMinutes(10);
        delivery.Postpone(until, "лимит");
        Assert.Equal((0, until), (delivery.Attempts, delivery.NextAttemptAt));
        Assert.Throws<InvalidOperationException>(() => delivery.Retry(DateTime.UtcNow));

        for (var i = 0; i < ScmDelivery.MaxAttempts; i++)
            delivery.MarkFailed("сбой", DateTime.UtcNow);
        Assert.Equal(ScmDeliveryStatus.Failed, delivery.Status);
        Assert.Throws<InvalidOperationException>(() => delivery.Postpone(until, "лимит"));

        var now = DateTime.UtcNow;
        delivery.Retry(now);
        Assert.Equal((ScmDeliveryStatus.Pending, 0, now, (string?)null), (delivery.Status, delivery.Attempts, delivery.NextAttemptAt, delivery.LastError));
    }

    [Fact]
    public void Bot_Is_A_Deactivated_Profile_And_Activity_Source_Is_Set_Once()
    {
        var bot = ScmBot.Create();
        Assert.Equal((ScmBot.Id, "flow-bot", UserStatus.Deactivated), (bot.Id, bot.Username, bot.Status));

        var activity = TaskActivity.StatusChanged(Guid.NewGuid(), bot.Id, Guid.NewGuid(), Guid.NewGuid()).FromSource(" PR #42 ", "https://h/pr/42");
        Assert.Equal(("PR #42", "https://h/pr/42"), (activity.Source, activity.SourceUrl));
        Assert.Throws<InvalidOperationException>(() => activity.FromSource("коммит a1", null));

        var binding = ScmRepositoryBoard.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(((Guid?)null, (Guid?)null, false), (binding.OnPullRequestOpenedStatusId, binding.OnPullRequestMergedStatusId, binding.SmartCommits));
        var status = Guid.NewGuid();
        binding.Configure(null, status, true);
        Assert.Equal(((Guid?)null, (Guid?)status, true), (binding.OnPullRequestOpenedStatusId, binding.OnPullRequestMergedStatusId, binding.SmartCommits));

        var link = ScmLink.Create(Guid.NewGuid(), Guid.NewGuid(), ScmLinkKind.PullRequest, "1");
        link.SetNote(new string('x', 600));
        Assert.Equal(ScmLink.NoteMaxLength, link.Note!.Length);
        link.SetNote(" ");
        Assert.Null(link.Note);
    }
}
