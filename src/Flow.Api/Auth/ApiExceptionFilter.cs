using Flow.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flow.Api.Auth;

/// <summary>
/// Общий перевод исключений Application в HTTP: недоступность Flow.Auth → 502 { message }. Сюда же лягут
/// ForbiddenException → 403 и UnauthorizedActorException → 401 из ТЗ ролей (#19). Ошибки валидации ввода
/// (ArgumentException/InvalidOperationException → 400) контроллеры по-прежнему ловят сами.
/// </summary>
public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not AuthUnavailableException ex)
            return;

        logger.LogError(ex, "Flow.Auth is unavailable");

        context.Result = new ObjectResult(new { Message = "Сервис входа недоступен. Попробуйте позже." })
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
        context.ExceptionHandled = true;
    }
}
