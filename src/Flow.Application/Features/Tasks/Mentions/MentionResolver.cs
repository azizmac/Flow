using Flow.Application.Abstractions;

namespace Flow.Application.Features.Tasks.Mentions;

/// <summary>Разбор @username из текста и резолв в UserId одним запросом. Неизвестные имена остаются просто текстом.</summary>
internal sealed class MentionResolver(IUserRepository users)
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(string body, CancellationToken cancellationToken)
    {
        var usernames = MentionParser.Parse(body);
        if (usernames.Count == 0)
            return [];

        var found = await users.GetByUsernamesAsync(usernames.ToList(), cancellationToken);
        return found.Select(u => u.Id).Distinct().ToList();
    }
}
