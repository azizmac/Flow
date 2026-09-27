using Flow.Shared.Contracts.Boards;

namespace Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;

/// <summary>Исходы команд участников: 404 (проекта или участника нет) / 400 (человек не найден или деактивирован) / 200.</summary>
public sealed class BoardMemberResult
{
    public bool IsNotFound { get; }

    public string? ValidationError { get; }

    public BoardMembersResponse? Response { get; }

    private BoardMemberResult(bool isNotFound, string? validationError, BoardMembersResponse? response)
    {
        IsNotFound = isNotFound;
        ValidationError = validationError;
        Response = response;
    }

    public static BoardMemberResult NotFound() => new(true, null, null);

    public static BoardMemberResult Invalid(string error) => new(false, error, null);

    public static BoardMemberResult Success(BoardMembersResponse response) => new(false, null, response);
}
