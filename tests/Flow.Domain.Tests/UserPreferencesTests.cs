using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class UserPreferencesTests
{
    [Fact]
    public void NewUser_Should_HaveDefaultPreferences()
    {
        var user = User.Create("ilya", "ilya@example.com", "Илья", "Моторин");

        Assert.Equal(SidebarMode.Auto, user.Preferences.SidebarMode);
        Assert.Equal(StartPage.Projects, user.Preferences.StartPage);
        Assert.Equal(UserPreferences.DefaultTasksPageSize, user.Preferences.TasksPageSize);
    }

    [Fact]
    public void Create_Should_RejectUnknownValues()
    {
        Assert.Throws<ArgumentException>(() => UserPreferences.Create((SidebarMode)42, StartPage.Projects, 100));
        Assert.Throws<ArgumentException>(() => UserPreferences.Create(SidebarMode.Auto, (StartPage)42, 100));
        Assert.Throws<ArgumentException>(() => UserPreferences.Create(SidebarMode.Auto, StartPage.Projects, 30));
        Assert.Throws<ArgumentException>(() => UserPreferences.Create(SidebarMode.Auto, StartPage.Projects, 0));
    }

    [Fact]
    public void With_Should_ReplaceOnlyGivenFields()
    {
        var preferences = UserPreferences.Create(SidebarMode.Collapsed, StartPage.MyTasks, 25);

        var changed = preferences.With(tasksPageSize: 50);

        Assert.Equal(SidebarMode.Collapsed, changed.SidebarMode);
        Assert.Equal(StartPage.MyTasks, changed.StartPage);
        Assert.Equal(50, changed.TasksPageSize);
        Assert.Equal(25, preferences.TasksPageSize);
    }

    [Fact]
    public void ChangePreferences_Should_ReportWhetherAnythingChanged()
    {
        var user = User.Create("ilya", "ilya@example.com", "Илья", "Моторин");

        Assert.False(user.ChangePreferences(UserPreferences.Create(SidebarMode.Auto, StartPage.Projects, 100)));
        Assert.True(user.ChangePreferences(UserPreferences.Create(SidebarMode.Expanded, StartPage.Projects, 100)));
        Assert.Equal(SidebarMode.Expanded, user.Preferences.SidebarMode);
    }
}
