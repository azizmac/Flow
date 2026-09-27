using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Дашборд (docs/TZ_task_views.md §8): имя, видимость, сетка 12 колонок, лимит виджетов.</summary>
public class DashboardTests
{
    private static Dashboard New(SavedFilterVisibility visibility = SavedFilterVisibility.Private) =>
        Dashboard.Create(Guid.NewGuid(), "  Мой  ", visibility);

    [Fact]
    public void Name_Is_Required_And_Trimmed()
    {
        Assert.Throws<ArgumentException>(() => Dashboard.Create(Guid.NewGuid(), " ", SavedFilterVisibility.Private));
        Assert.Throws<ArgumentException>(() => Dashboard.Create(Guid.NewGuid(), new string('x', Dashboard.NameMaxLength + 1), SavedFilterVisibility.Private));
        Assert.Throws<ArgumentException>(() => Dashboard.Create(Guid.Empty, "Мой", SavedFilterVisibility.Private));

        Assert.Equal("Мой", New().Name);
    }

    [Fact]
    public void Private_Is_Visible_Only_To_Owner_Shared_To_Everyone()
    {
        var mine = New();
        var shared = New(SavedFilterVisibility.Shared);

        Assert.True(mine.IsVisibleTo(mine.OwnerId));
        Assert.False(mine.IsVisibleTo(Guid.NewGuid()));
        Assert.True(shared.IsVisibleTo(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1, 0, 6, 3)]
    [InlineData(0, 0, 0, 3)]
    [InlineData(7, 0, 6, 3)]
    [InlineData(0, -1, 6, 3)]
    [InlineData(0, 0, 6, 0)]
    [InlineData(0, 0, 6, 9)]
    public void Widget_Must_Fit_The_Grid(int x, int y, int w, int h) =>
        Assert.Throws<ArgumentException>(() => New().AddWidget(WidgetType.Counter, null, "{}", x, y, w, h));

    [Fact]
    public void New_Widget_Without_Position_Takes_The_First_Free_Slot()
    {
        var dashboard = New();
        dashboard.AddWidget(WidgetType.Counter, null, "{}", 0, 0, 6, 2);
        dashboard.AddWidget(WidgetType.Counter, null, "{}", 6, 1, 6, 4);

        var beside = dashboard.AddWidget(WidgetType.Counter, null, "{}", w: 3, h: 1);
        var below = dashboard.AddWidget(WidgetType.Markdown, "Заметка", "{}", w: 8);

        Assert.Equal((6, 0), (beside.X, beside.Y));
        Assert.Equal((0, 5), (below.X, below.Y));
    }

    [Fact]
    public void Config_Must_Be_A_Json_Object_And_Title_Is_Limited()
    {
        var dashboard = New();

        Assert.Throws<ArgumentException>(() => dashboard.AddWidget(WidgetType.Counter, null, "[]"));
        Assert.Throws<ArgumentException>(() => dashboard.AddWidget(WidgetType.Counter, new string('x', DashboardWidget.TitleMaxLength + 1), "{}"));
        Assert.Throws<ArgumentException>(() => dashboard.AddWidget((WidgetType)99, null, "{}"));
    }

    [Fact]
    public void At_Most_Thirty_Widgets_And_Foreign_Widget_Is_Rejected()
    {
        var dashboard = New();
        for (var i = 0; i < Dashboard.MaxWidgets; i++)
            dashboard.AddWidget(WidgetType.Counter, null, "{}");

        Assert.Throws<InvalidOperationException>(() => dashboard.AddWidget(WidgetType.Counter, null, "{}"));
        Assert.Throws<InvalidOperationException>(() => dashboard.RemoveWidget(Guid.NewGuid()));

        dashboard.RemoveWidget(dashboard.Widgets.First().Id);
        Assert.Equal(Dashboard.MaxWidgets - 1, dashboard.Widgets.Count);
    }

    [Fact]
    public void Update_Widget_Moves_And_Resizes()
    {
        var dashboard = New();
        var widget = dashboard.AddWidget(WidgetType.Counter, null, "{}");

        dashboard.UpdateWidget(widget.Id, "Открытые", "{\"fql\":\"\"}", 4, 2, 8, 5);

        Assert.Equal(("Открытые", 4, 2, 8, 5), (widget.Title, widget.X, widget.Y, widget.W, widget.H));
    }
}
