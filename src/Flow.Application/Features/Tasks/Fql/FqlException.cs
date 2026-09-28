namespace Flow.Application.Features.Tasks.Fql;

/// <summary>
/// Ошибка в строке FQL (docs/TZ_task_views.md §7): синтаксис или биндинг имени. Position/Length — место в исходной
/// строке, клиент подчёркивает его. Текст — по-русски: его показывают человеку как есть. Контроллер отвечает 400.
/// </summary>
public sealed class FqlException(string message, int position, int length) : Exception(message)
{
    public int Position { get; } = position;

    public int Length { get; } = Math.Max(1, length);
}
