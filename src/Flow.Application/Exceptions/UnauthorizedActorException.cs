namespace Flow.Application.Exceptions;

/// <summary>
/// Действовать некому: в токене нет sub, профиля с таким Id нет или он деактивирован. Контроллер переводит в 401.
/// Отдельно от <see cref="ForbiddenException"/>: там actor известен, но прав не хватает.
/// </summary>
public sealed class UnauthorizedActorException(string message) : Exception(message);
