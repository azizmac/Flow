namespace Flow.Shared.Contracts.Boards;

/// <summary>Зеркало Flow.Domain.Entities.BoardVisibility: Open — все по роли во Flow, Private — только участники и Admin/Owner.</summary>
public enum BoardVisibility
{
    Open = 0,
    Private = 1
}
