using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Flow.Api.Routing;

/// <summary>
/// Переносит все <c>[ApiController]</c> под префикс: <c>boards</c> → <c>api/boards</c>.
///
/// Зачем: интерфейс переехал в этот же хост, поэтому маршруты страниц и маршруты контроллеров
/// попадают в один роутер, а восемь шаблонов у них совпадают дословно — <c>/</c>, <c>/boards</c>,
/// <c>/boards/{id}</c>, <c>/tasks</c>, <c>/tasks/{id}</c>, <c>/users</c>, <c>/users/{id}</c>,
/// <c>/search</c>.
///
/// Именно ЗАМЕНА, а не добавление второго маршрута рядом. Сначала здесь была совместимость —
/// старый путь и префиксованный работали одновременно, чтобы уже выкаченная сборка WASM оставалась
/// откатом. Так нельзя: старый <c>/boards</c> сталкивается со страницей <c>/boards</c> того же
/// хоста, и запрос падает с AmbiguousMatchException (проверено запуском: 500 на /boards, /users
/// и /search). Откат у этого перехода всё равно на уровне образа, а не маршрута.
///
/// <c>/connect/*</c>, <c>/account/*</c> и <c>/health/*</c> префикса не получают: страницы их не занимают.
/// </summary>
internal sealed class ApiPrefixConvention(string prefix) : IApplicationModelConvention
{
    private readonly AttributeRouteModel _prefix = new(new RouteAttribute(prefix));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            // Страницы Auth-модуля (Razor Pages) сюда не попадают, но [ApiController] проверяем явно:
            // префикс предназначен только для JSON-API, а не для /connect/* и /account/*.
            if (!controller.Attributes.OfType<ApiControllerAttribute>().Any())
                continue;

            foreach (var selector in controller.Selectors)
            {
                // AttributeRouteModel == null у контроллеров без [Route] (Tasks, Comments, Attachments):
                // там маршруты висят на действиях, и MVC комбинирует их с маршрутом контроллера.
                selector.AttributeRouteModel = selector.AttributeRouteModel is null
                    ? _prefix
                    : AttributeRouteModel.CombineAttributeRouteModel(_prefix, selector.AttributeRouteModel);
            }
        }
    }
}
