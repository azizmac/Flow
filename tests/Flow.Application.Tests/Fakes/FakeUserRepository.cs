using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(u => u.Id == id));

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(u => u.Username == username));

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Any(u => u.Username == username));

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Any(u => u.Email == email));

    public Task<IReadOnlyList<User>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(_users
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.Username)
            .ToList());

    public Task<IReadOnlyList<User>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(_users
            .Where(u => u.IsActive)
            .Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || u.FirstName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || u.LastName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Username)
            .Take(limit)
            .ToList());

    public void Add(User user) => _users.Add(user);
}
