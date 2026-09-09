using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

/// <summary>Links — owned-коллекция, EF подгружает её автоматически, явный Include не нужен.</summary>
public sealed class UserRepository(FlowDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Username == username, cancellationToken);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.Users
            .Where(u => includeInactive || u.Status != UserStatus.Deactivated)
            .OrderBy(u => u.Username)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        // ILIKE регистронезависим на стороне Postgres; % и _ в запросе экранируем, чтобы они не работали как wildcard.
        var pattern = $"%{EscapeLike(query)}%";

        return await db.Users
            .Where(u => u.Status != UserStatus.Deactivated)
            .Where(u => EF.Functions.ILike(u.Username, pattern, "\\")
                        || EF.Functions.ILike(u.FirstName, pattern, "\\")
                        || EF.Functions.ILike(u.LastName, pattern, "\\"))
            .OrderBy(u => u.Username)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountByRoleAsync(UserRole role, CancellationToken cancellationToken) =>
        db.Users.CountAsync(u => u.Role == role, cancellationToken);

    public void Add(User user) => db.Users.Add(user);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
