using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Users;

/// <summary>
/// Где лежит аватар и каким типом он отдаётся. Объект — <c>avatars/{userId}/{имя}</c> в том же хранилище,
/// что и вложения; имя файла пользователя в ключ не попадает (как и у вложений: кириллица, пробелы, «../»).
/// </summary>
internal static class UserAvatars
{
    /// <summary>Только растр, который сверяется по сигнатуре: SVG — это разметка со скриптами, а не картинка.</summary>
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp"
    };

    public static string StorageKey(Guid userId, string fileName) => $"avatars/{userId}/{fileName}";

    /// <summary>Расширение по типу; null — тип аватаром быть не может.</summary>
    public static string? ExtensionFor(string contentType) =>
        Extensions.TryGetValue(contentType, out var extension) ? extension : null;

    /// <summary>Тип по имени файла, которое выдал сам Flow (<see cref="User.SetAvatar"/> пускает только эти расширения).</summary>
    public static string ContentTypeOf(string fileName) => FileTypes.FromFileName(fileName);

    /// <summary>Хранилище недоступно — объект останется мусором; ронять из-за этого уже сохранённую правку незачем.</summary>
    public static async Task SafeDeleteAsync(IFileStorage storage, Guid userId, string? fileName, CancellationToken cancellationToken)
    {
        if (fileName is null)
            return;

        try
        {
            await storage.DeleteAsync(StorageKey(userId, fileName), cancellationToken);
        }
        catch
        {
            // best-effort, см. выше
        }
    }
}
