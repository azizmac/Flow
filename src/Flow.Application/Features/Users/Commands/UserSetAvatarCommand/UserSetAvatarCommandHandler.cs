using Flow.Application.Abstractions;
using Flow.Application.Features.Attachments;
using Flow.Application.Security;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserSetAvatarCommand;

/// <summary>
/// Порядок тот же, что у вложений: объект в хранилище → строка в БД → старый объект удаляется best-effort.
/// Имя каждый раз новое: адрес картинки версионный, и старый не может показать новую картинку из кэша.
/// Индекс поиска не трогаем — картинки в чанке человека нет.
/// </summary>
internal sealed class UserSetAvatarCommandHandler(ActorResolver actors, IFileStorage storage, IUnitOfWork unitOfWork)
    : IRequestHandler<UserSetAvatarCommand, UserResponse>
{
    public async Task<UserResponse> Handle(UserSetAvatarCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        if (request.SizeBytes <= 0)
            throw new ArgumentException("Пустой файл аватаром быть не может.", nameof(request.Content));

        if (request.SizeBytes > AvatarLimits.MaxBytes)
            throw new ArgumentException($"Аватар не больше {AvatarLimits.MaxBytes / (1024 * 1024)} МБ.", nameof(request.Content));

        var contentType = FileTypes.FromFileName(request.FileName);
        var extension = UserAvatars.ExtensionFor(contentType)
                        ?? throw new ArgumentException("Аватар — картинка PNG, JPEG, GIF или WebP.", nameof(request.FileName));

        // Расширению не верим: .png, внутри которого HTML, картинкой не станет.
        request.Content.Position = 0;
        var head = new byte[FileTypes.SignatureLength];
        var headLength = await request.Content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (!FileTypes.MatchesSignature(contentType, head.AsSpan(0, headLength)))
            throw new ArgumentException("Файл не похож на картинку своего формата.", nameof(request.Content));

        var previous = actor.UploadedAvatarFileName();
        var fileName = $"{Guid.NewGuid():N}{extension}";

        request.Content.Position = 0;
        await storage.PutAsync(UserAvatars.StorageKey(actor.Id, fileName), request.Content, contentType, cancellationToken);

        try
        {
            actor.SetAvatar(fileName);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await UserAvatars.SafeDeleteAsync(storage, actor.Id, fileName, cancellationToken);
            throw;
        }

        await UserAvatars.SafeDeleteAsync(storage, actor.Id, previous, cancellationToken);
        return actor.ToResponse();
    }
}
