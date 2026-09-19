using System.Net;
using System.Security.Claims;
using Flow.Application.Exceptions;
using Flow.Client.Services;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;

namespace Flow.Api.Client;

/// <summary>
/// Реализация <see cref="IFlowApi"/> для серверного рендера: страницы исполняются в том же процессе,
/// что и Flow.Application, поэтому ходить к самим себе по HTTP незачем — ни сериализации, ни CORS,
/// ни префлайтов, ни токена в заголовке.
///
/// Почему не HttpClient на loopback: в circuit'е Blazor Server нет HttpContext, cookie flow.auth
/// у неё HttpOnly, а положить её в PersistentComponentState нельзя — это утечка cookie в разметку.
/// Личность берём из AuthenticationStateProvider: он в circuit'е работает штатно.
///
/// Коды ответов повторяют контроллеры один в один (ApiExceptionFilter + их собственные catch):
/// на Ok/NotFound/Conflict/Unauthorized/Forbidden завязаны все экраны.
/// </summary>
internal sealed partial class InProcessFlowApi(IMediator mediator, AuthenticationStateProvider auth) : IFlowApi
{
    /// <summary>
    /// Id текущего пользователя. sub — от токена OpenIddict (осталось для совместимости),
    /// NameIdentifier — то, что кладёт cookie-схема Identity после /account/login.
    /// </summary>
    private async Task<Guid> ActorAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        var raw = state.User.FindFirst("sub")?.Value
                  ?? state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedActorException("Нужно войти заново.");
    }

    private static ApiResult<T> Ok<T>(T value) => ApiResult<T>.Success(value, HttpStatusCode.OK);

    private static ApiResult<T> NotFound<T>() => ApiResult<T>.Fail("Не найдено.", HttpStatusCode.NotFound);

    /// <summary>404 как штатный результат: у HTTP-клиента Delete/SendNoContent возвращали Success(false).</summary>
    private static ApiResult<bool> Missing() => ApiResult<bool>.Success(false, HttpStatusCode.NotFound);

    private static ApiResult<T> Conflict<T>(string message) => ApiResult<T>.Fail(message, HttpStatusCode.Conflict);

    private static ApiResult<T> Invalid<T>(string message) => ApiResult<T>.Fail(message, HttpStatusCode.BadRequest);

    /// <summary>
    /// Тот же перевод исключений в коды, что делают ApiExceptionFilter (401/403) и сами контроллеры
    /// (ArgumentException / InvalidOperationException → 400). Без него страницы получали бы голое
    /// исключение вместо человекочитаемой ошибки в тосте.
    /// </summary>
    private static async Task<ApiResult<T>> Guard<T>(Func<Task<ApiResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedActorException ex)
        {
            return ApiResult<T>.Fail(ex.Message, HttpStatusCode.Unauthorized);
        }
        catch (ForbiddenException ex)
        {
            return ApiResult<T>.Fail(ex.Message, HttpStatusCode.Forbidden);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<T>.Fail(ex.Message, HttpStatusCode.BadRequest);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResult<T>.Fail(ex.Message, HttpStatusCode.BadRequest);
        }
    }
}
