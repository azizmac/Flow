using Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;
using Flow.Application.Features.Attachments.Queries.AttachmentMetaQuery;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Прямые ссылки на содержимое вложений для самих страниц: <c>&lt;img src="/files/{id}"&gt;</c> и
/// <c>&lt;a href="/files/{id}" download&gt;</c> работают без единой строки JS, и файл не идёт через
/// circuit — раньше байты приезжали на сервер в память и оттуда уходили в браузер по SignalR.
///
/// Почему отдельный контроллер, а не действие в AttachmentsController:
/// — тег img заголовков не носит, он пошлёт только cookie, а под /api принимается только Bearer
///   (схема-диспетчер AuthConstants.SmartScheme). Значит маршрут обязан быть вне префикса;
/// — [ApiController] здесь намеренно НЕТ: ApiPrefixConvention вешает /api на все такие контроллеры.
///   Шаблон дополнительно начинается со слэша — такой маршрут не комбинируется с префиксом ни при
///   каких условиях, даже если атрибут когда-нибудь допишут по привычке.
///
/// Точнее про схему: маршрут не «по cookie», а двухсхемный. Селектор отправляет в Bearer любой запрос
/// с заголовком Authorization независимо от пути, а всё остальное — в cookie. Анонимно не отдаётся:
/// FallbackPolicy в Program.cs закрывает всё без явного [AllowAnonymous]. Неаутентифицированный
/// браузер получит 302 на /account/login?ReturnUrl=/files/{id} и после входа попадёт прямо на файл.
///
/// Круг читателей прямые ссылки не расширяют: проверки прав на чтение вложения в Flow нет и не было
/// (см. AttachmentContentQueryHandler), id легально приходит клиенту в списке вложений задачи.
/// Что действительно меняется — у файла появляется копируемый адрес: см. docs/TZ_attachments.md.
/// </summary>
public sealed class FilesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Содержимое вложения. Тип отдачи выбирает сервер: картинки из белого списка показываются
    /// в браузере, остальное скачивается. Параметра запроса нет намеренно — у <c>[FromQuery] bool</c>
    /// есть ловушка: «inline=1» в bool не биндится, и без <c>[ApiController]</c> это даже не даст 400,
    /// а молча станет false и превратит картинку в скачивание. Принудительное сохранение задаётся
    /// атрибутом download у ссылки, на стороне браузера.
    /// </summary>
    [HttpGet("/files/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        // Сначала строка, потом объект в хранилище. Порядок принципиален: строка стоит микросекунды
        // и отвечает на вопрос «файл ещё существует», а поход в S3 — самое дорогое здесь, и при
        // попадании в кэш браузера его надо избежать.
        var meta = await mediator.Send(new AttachmentMetaQuery(id), cancellationToken);
        if (meta is null)
            return NotFound();

        var etag = AttachmentDelivery.ETagOf(meta.ContentHash);
        AttachmentDelivery.ApplyHeaders(this, meta.FileName, meta.CanInline, forceDownload: false, etag);

        if (AttachmentDelivery.Matches(this, etag))
            return StatusCode(StatusCodes.Status304NotModified);

        var content = await mediator.Send(new AttachmentContentQuery(id), cancellationToken);
        if (content is null)
            return NotFound();

        return AttachmentDelivery.Send(this, content);
    }

    /// <summary>
    /// Ловушка для мусора в адресе. Без неё «/files/не-guid» не совпадает с шаблоном выше, доезжает
    /// до фоллбэка Razor Components и отдаёт HTML-страницу «не найдено» — а ссылка с атрибутом
    /// download сохранила бы эту страницу под именем файла.
    /// </summary>
    [HttpGet("/files/{**rest}")]
    public IActionResult NotAFile() => NotFound();
}
