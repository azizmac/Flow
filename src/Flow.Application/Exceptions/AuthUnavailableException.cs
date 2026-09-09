namespace Flow.Application.Exceptions;

/// <summary>Flow.Auth не отвечает или отвечает 5xx/401 на служебный вызов. Контроллер переводит в 502 «Сервис входа недоступен».</summary>
public sealed class AuthUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
