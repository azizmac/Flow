using Flow.Shared.Contracts.Attachments;

namespace Flow.Client.Services;

/// <summary>Что случилось с вложениями задачи: файл приложен или удалён.</summary>
/// <param name="Added">Появившееся вложение; null — речь об удалении.</param>
/// <param name="RemovedId">Удалённое вложение; null — речь о добавлении.</param>
public sealed record AttachmentChange(Guid TaskId, AttachmentResponse? Added, Guid? RemovedId);

/// <summary>
/// Файл можно приложить из трёх мест: кнопкой в блоке вложений, перетаскиванием в карточку и прямо
/// из редактора комментария. Владелец у файла один — задача, поэтому список вложений должен обновиться
/// независимо от того, откуда его приложили. Ссылки между компонентами ради этого не годятся: они
/// живут в разных ветках дерева и появляются в разном порядке.
///
/// Событие несёт сам файл, а не просьбу перечитать список: иначе загрузка картинки из комментария
/// заставляла бы список заново качать все превью.
/// </summary>
public sealed class AttachmentEvents
{
    public event Action<AttachmentChange>? Changed;

    public void NotifyAdded(Guid taskId, AttachmentResponse attachment) =>
        Changed?.Invoke(new AttachmentChange(taskId, attachment, null));

    public void NotifyRemoved(Guid taskId, Guid attachmentId) =>
        Changed?.Invoke(new AttachmentChange(taskId, null, attachmentId));
}
