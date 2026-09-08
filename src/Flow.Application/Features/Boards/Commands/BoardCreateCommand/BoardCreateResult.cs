using Flow.Shared.Contracts.Boards;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>
/// Занятый ключ доски отличается от невалидного ключа HTTP-ответом (409 вместо 400),
/// поэтому вместо исключения используется явный результат (по аналогии с TaskUpdateResult).
/// </summary>
public sealed class BoardCreateResult
{
    public bool IsKeyTaken { get; }

    public string? ValidationError { get; }

    public BoardResponse? Response { get; }

    private BoardCreateResult(bool isKeyTaken, string? validationError, BoardResponse? response)
    {
        IsKeyTaken = isKeyTaken;
        ValidationError = validationError;
        Response = response;
    }

    public static BoardCreateResult KeyTaken(string key) =>
        new(true, $"Board key '{key}' is already in use.", null);

    public static BoardCreateResult Success(BoardResponse response) => new(false, null, response);
}
