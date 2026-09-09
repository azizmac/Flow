namespace Flow.Shared.Contracts.Users;

public sealed record CreateUserRequest(string Username, string Email, string FirstName, string LastName);
