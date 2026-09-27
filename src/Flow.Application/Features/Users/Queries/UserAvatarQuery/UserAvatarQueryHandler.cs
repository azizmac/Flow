using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserAvatarQuery;

internal sealed class UserAvatarQueryHandler(IUserRepository users, IFileStorage storage)
    : IRequestHandler<UserAvatarQuery, UserAvatarContent?>
{
    public async Task<UserAvatarContent?> Handle(UserAvatarQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken);

        // Отдаём только текущий файл: сверка с профилем заодно отсекает любые подобранные имена,
        // и в хранилище по чужому ключу запрос не уйдёт.
        if (user?.UploadedAvatarFileName() is not { } fileName || fileName != request.FileName)
            return null;

        var content = await storage.OpenReadAsync(UserAvatars.StorageKey(user.Id, fileName), cancellationToken);
        return content is null ? null : new UserAvatarContent(content, UserAvatars.ContentTypeOf(fileName));
    }
}
