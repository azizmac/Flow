using Flow.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flow.Api.Auth;

/// <summary>
/// Общий перевод исключений Application в HTTP: UnauthorizedActorException → 401 (нет actor'а или он деактивирован),
/// ForbiddenException → 403 (роль не позволяет), недоступность Flow.Auth → 502. Все — { message }.
/// Ошибки валидации ввода (ArgumentException/InvalidOperationException → 400) контроллеры по-прежнему ловят сами.
/// </summary>
public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch
        {
            UnauthorizedActorException ex => (StatusCodes.Status401Unauthorized, ex.Message),
            ForbiddenException ex => (StatusCodes.Status403Forbidden, ex.Message),
            AuthUnavailableException => (StatusCodes.Status502BadGateway, "Сервис входа недоступен. Попробуйте позже."),
            _ => (0, string.Empty)
        };

        if (status == 0)
            return;

        if (status == StatusCodes.Status502BadGateway)
            logger.LogError(context.Exception, "Flow.Auth is unavailable");

        context.Result = new ObjectResult(new { Message = message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
