namespace Flow.Shared.Contracts.Users;

/// <summary>POST /users/{id}/password. CurrentPassword = null — сброс без проверки текущего (только Owner).</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string NewPassword);
