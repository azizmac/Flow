using Flow.Shared.Contracts.Attachments;

namespace Flow.Client.Services;

/// <summary>
/// Размеры картинок-вложений, известные этой сессии: id → отображаемые ширина и высота.
///
/// Зачем отдельный справочник. В тексте описания и комментария лежит «attachment:{id}» и ничего кроме,
/// поэтому рендер Markdown знает про картинку только её id — а размеры живут в строке вложения, которую
/// грузит совсем другой компонент. Через этот справочник они и встречаются.
///
/// Наполнять его обязательно ДО первого рендера текста. Если размеры приедут после, браузер к тому
/// моменту уже разошлёт запросы на все картинки ленты, и чинить будет нечего: весь смысл затеи —
/// правильная разметка на первом проходе.
///
/// Scoped, а не Singleton: при серверном рендере singleton — это один объект на всех пользователей.
/// </summary>
public sealed class AttachmentSizes
{
    private readonly Dictionary<Guid, (int Width, int Height)> _known = [];

    /// <summary>Сообщает, что справочник пополнился: текст, отрисованный без размеров, стоит пересобрать.</summary>
    public event Action? Changed;

    public (int Width, int Height)? Find(Guid id) =>
        _known.TryGetValue(id, out var size) ? size : null;

    /// <summary>
    /// Запоминает размеры из списка вложений. Возвращает true, если что-то появилось впервые —
    /// по нему вызывающий решает, дёргать ли перерисовку.
    /// </summary>
    public bool Remember(IEnumerable<AttachmentResponse> attachments)
    {
        var added = false;

        foreach (var item in attachments)
        {
            // Пара или ничего: одинокая сторона браузеру бесполезна, соотношение он считает из обеих.
            if (item.Width is not { } width || item.Height is not { } height || width <= 0 || height <= 0)
                continue;

            if (_known.TryAdd(item.Id, (width, height)))
                added = true;
        }

        if (added)
            Changed?.Invoke();

        return added;
    }
}
