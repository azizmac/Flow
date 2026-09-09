namespace Flow.Application.Exceptions;

/// <summary>Actor есть, но роль не позволяет действие. Контроллер переводит в 403 { message }. Текст — для пользователя.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
