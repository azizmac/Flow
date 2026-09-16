using Flow.Application.Features.Search;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Разбор строки поиска: по тесту на каждый распознаваемый фильтр плюс ложные срабатывания.
/// Нераспознанное обязано остаться свободным текстом — иначе запрос молча меняет смысл.
/// </summary>
public class QueryIntentParserTests
{
    [Fact]
    public void Plain_Text_Stays_Text()
    {
        var intent = QueryIntentParser.Parse("падает экспорт отчёта");

        Assert.Equal("падает экспорт отчёта", intent.Text);
        Assert.False(intent.HasFilters);
    }

    [Fact]
    public void Empty_Query_Is_Empty_Intent()
    {
        var intent = QueryIntentParser.Parse("   ");

        Assert.Equal("", intent.Text);
        Assert.False(intent.HasFilters);
    }

    [Fact]
    public void Task_Code_Is_Recognized_And_Uppercased()
    {
        var intent = QueryIntentParser.Parse("proj-142 падает");

        Assert.Equal("PROJ-142", intent.TaskCode);
        Assert.Equal("падает", intent.Text);
    }

    [Fact]
    public void Assignee_Is_Recognized_And_Lowercased()
    {
        var intent = QueryIntentParser.Parse("@Ivanov экспорт");

        Assert.Equal("ivanov", intent.AssigneeUsername);
        Assert.Equal("экспорт", intent.Text);
    }

    [Fact]
    public void Email_And_Path_Are_Not_Assignees()
    {
        var email = QueryIntentParser.Parse("ilya@example.com");
        var path = QueryIntentParser.Parse("docs/@readme");

        Assert.Null(email.AssigneeUsername);
        Assert.Null(path.AssigneeUsername);
        Assert.Equal("ilya@example.com", email.Text);
    }

    [Fact]
    public void Board_Filter_Is_Recognized()
    {
        var intent = QueryIntentParser.Parse("проект:DBACK авторизация");

        Assert.Equal("DBACK", intent.BoardKey);
        Assert.Equal("авторизация", intent.Text);
    }

    [Fact]
    public void Status_Filter_Takes_Two_Words()
    {
        var intent = QueryIntentParser.Parse("экспорт статус:в работе");

        Assert.Equal("в работе", intent.StatusName);
        Assert.Equal("экспорт", intent.Text);
    }

    [Fact]
    public void Quoted_Status_Takes_Everything_Inside_Quotes()
    {
        var intent = QueryIntentParser.Parse("статус:\"готово к релизу\" экспорт");

        Assert.Equal("готово к релизу", intent.StatusName);
        Assert.Equal("экспорт", intent.Text);
    }

    [Fact]
    public void Mine_And_Overdue_Are_Flags()
    {
        var intent = QueryIntentParser.Parse("мои просроченные");

        Assert.True(intent.Mine);
        Assert.True(intent.Overdue);
        Assert.Equal("", intent.Text);
        Assert.True(intent.HasFilters);
    }

    [Fact]
    public void Overdue_Matches_Other_Forms()
    {
        Assert.True(QueryIntentParser.Parse("просроченная").Overdue);
        Assert.True(QueryIntentParser.Parse("просрочено").Overdue);
    }

    [Theory]
    [InlineData("за день", 1)]
    [InlineData("за неделю", 7)]
    [InlineData("за месяц", 30)]
    [InlineData("за год", 365)]
    public void Period_Is_Recognized(string query, int days)
    {
        var intent = QueryIntentParser.Parse(query + " экспорт");

        Assert.Equal(TimeSpan.FromDays(days), intent.Period);
        Assert.Equal("экспорт", intent.Text);
    }

    [Fact]
    public void Everything_Together()
    {
        var intent = QueryIntentParser.Parse("падает экспорт @ivanov проект:DBACK мои просроченные за неделю");

        Assert.Equal("падает экспорт", intent.Text);
        Assert.Equal("ivanov", intent.AssigneeUsername);
        Assert.Equal("DBACK", intent.BoardKey);
        Assert.True(intent.Mine);
        Assert.True(intent.Overdue);
        Assert.Equal(TimeSpan.FromDays(7), intent.Period);
    }

    [Fact]
    public void Words_That_Only_Look_Like_Filters_Stay_Text()
    {
        // «моих» — не «мои», «задень» — не «за день», дефис в середине слова — не код задачи.
        var intent = QueryIntentParser.Parse("моих задень бизнес-логика");

        Assert.False(intent.Mine);
        Assert.Null(intent.Period);
        Assert.Null(intent.TaskCode);
        Assert.Equal("моих задень бизнес-логика", intent.Text);
    }
}
