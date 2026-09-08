using System.Text.Json;
using Flow.Domain.Entities;
using Flow.Shared.Ids;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>
/// Типизированные id живут в Flow.Shared/Ids (в Domain.Tests они доступны транзитивно через Flow.Domain) —
/// тестируем их здесь, чтобы не заводить отдельный проект Flow.Shared.Tests.
/// </summary>
public class TypedIdTests
{
    [Fact]
    public void ToString_Should_FormatAsPrefixUnderscoreGuid()
    {
        var value = Guid.NewGuid();

        Assert.Equal($"boa_{value:N}", new BoardId(value).ToString());
        Assert.Equal($"tas_{value:N}", new TaskId(value).ToString());
        Assert.Equal($"sta_{value:N}", new StatusId(value).ToString());
    }

    [Fact]
    public void Entities_Should_ExposeTypedIdsWithOwnPrefixes()
    {
        var board = Board.Create("Flow Project", "FLW");
        var task = board.CreateTask("Test task");

        Assert.StartsWith("boa_", board.Id.ToString());
        Assert.StartsWith("tas_", task.Id.ToString());
        Assert.All(board.Statuses, s => Assert.StartsWith("sta_", s.Id.ToString()));
        Assert.Equal(board.Id, task.BoardId);
    }

    [Fact]
    public void TryParse_Should_RoundTrip()
    {
        var boardId = BoardId.New();
        var taskId = TaskId.New();
        var statusId = StatusId.New();

        Assert.True(BoardId.TryParse(boardId.ToString(), out var parsedBoardId));
        Assert.True(TaskId.TryParse(taskId.ToString(), out var parsedTaskId));
        Assert.True(StatusId.TryParse(statusId.ToString(), out var parsedStatusId));

        Assert.Equal(boardId, parsedBoardId);
        Assert.Equal(taskId, parsedTaskId);
        Assert.Equal(statusId, parsedStatusId);
    }

    [Fact]
    public void TryParse_Should_ReturnFalse_When_PrefixBelongsToAnotherEntity()
    {
        // Главная защита подхода: BoardId нельзя случайно собрать из строки задачи.
        Assert.False(BoardId.TryParse(TaskId.New().ToString(), out _));
        Assert.False(TaskId.TryParse(StatusId.New().ToString(), out _));
        Assert.False(StatusId.TryParse(BoardId.New().ToString(), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("boa")]
    [InlineData("boa_")]
    [InlineData("boa_not-a-guid")]
    [InlineData("BOA_9d0f1c2e4b7a4d3e8f1a2b3c4d5e6f70")]
    [InlineData("xyz_9d0f1c2e4b7a4d3e8f1a2b3c4d5e6f70")]
    [InlineData("9d0f1c2e4b7a4d3e8f1a2b3c4d5e6f70")]
    public void TryParse_Should_ReturnFalse_When_RawIsInvalid(string? raw)
    {
        Assert.False(BoardId.TryParse(raw, out _));
    }

    [Fact]
    public void Parse_Should_Throw_When_RawIsInvalid()
    {
        var ex = Assert.Throws<ArgumentException>(() => BoardId.Parse("tas_9d0f1c2e4b7a4d3e8f1a2b3c4d5e6f70"));

        Assert.Contains("boa_", ex.Message);
    }

    [Fact]
    public void Json_Should_RoundTripThroughConverter()
    {
        var statusId = StatusId.New();

        var json = JsonSerializer.Serialize(statusId);
        var deserialized = JsonSerializer.Deserialize<StatusId>(json);

        Assert.Equal($"\"{statusId}\"", json);
        Assert.Equal(statusId, deserialized);
    }

    [Fact]
    public void Json_Should_Throw_When_PrefixIsWrong()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StatusId>($"\"{TaskId.New()}\""));
    }

    [Fact]
    public void Json_Should_SerializeNullableIdAsNull()
    {
        var request = new Flow.Shared.Contracts.Tasks.UpdateTaskRequest("Title", null, null);

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"StatusId\":null", json);
    }
}
