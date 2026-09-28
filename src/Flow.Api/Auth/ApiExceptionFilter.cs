using Flow.Api.Controllers;
using Flow.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flow.Api.Auth;

/// <summary>
/// Общий перевод исключений Application в HTTP: UnauthorizedActorException → 401 (нет actor'а или он деактивирован),
/// ForbiddenException → 403 (роль не позволяет), ProjectNotFoundException → 404 (проект скрыт). Все — { message }.
/// Ошибки валидации ввода (ArgumentException/InvalidOperationException → 400) контроллеры по-прежнему ловят сами.
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch
        {
            UnauthorizedActorException ex => (StatusCodes.Status401Unauthorized, ex.Message),
            ForbiddenException ex => (StatusCodes.Status403Forbidden, ex.Message),
            ProjectNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            _ => (0, string.Empty)
        };

        if (status == 0)
            return;

        context.Result = new ObjectResult(new ApiError(message)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
