using Flow.Domain.Entities;

namespace Flow.Domain.Templates;

/// <summary>Встроенный шаблон проекта: живёт в коде, в БД его нет; Id постоянный — на него ссылается создание проекта.</summary>
public sealed record BuiltInBoardTemplate(Guid Id, string Name, string Description, BoardBlueprint Blueprint);

/// <summary>
/// Встроенные шаблоны проектов (docs/TZ_workflow_config.md §4). «Простой» — то, что получает проект без шаблона
/// (<see cref="DefaultStatuses"/> и <see cref="DefaultTaskTypes"/>, свободные переходы).
/// </summary>
public static class BuiltInBoardTemplates
{
    public static readonly Guid SimpleId = new("0f10a000-0000-4000-8000-000000000001");
    public static readonly Guid KanbanId = new("0f10a000-0000-4000-8000-000000000002");
    public static readonly Guid ScrumId = new("0f10a000-0000-4000-8000-000000000003");
    public static readonly Guid BugTrackerId = new("0f10a000-0000-4000-8000-000000000004");

    private static IReadOnlyList<BlueprintTaskType> DefaultTypes() =>
        DefaultTaskTypes.All.Select(t => new BlueprintTaskType(t.Kind.ToString().ToLowerInvariant(), t.Name, t.Kind, t.IsDefault)).ToList();

    public static readonly IReadOnlyList<BuiltInBoardTemplate> All =
    [
        new(SimpleId, "Простой", "Четыре статуса от «Не начата» до «Сделана», переходы свободные.",
            new BoardBlueprint(
                DefaultStatuses.All.Select(s => new BlueprintStatus(s.Type.ToString().ToLowerInvariant(), s.Name, s.Type, s.IsInitial, s.IsFinal)).ToList(),
                WorkflowMode.Free, [], DefaultTypes(), [], [])),

        new(KanbanId, "Kanban", "Поток «Бэклог → Готово» с WIP-лимитами на колонках в работе.",
            new BoardBlueprint(
            [
                new("backlog", "Бэклог", StatusType.NotStarted, IsInitial: true, IsFinal: false),
                new("todo", "К работе", StatusType.NotStarted, false, false, WipLimit: 10),
                new("doing", "В работе", StatusType.InProgress, false, false, WipLimit: 5),
                new("review", "Проверка", StatusType.InReview, false, false, WipLimit: 3),
                new("done", "Готово", StatusType.Done, false, IsFinal: true)
            ], WorkflowMode.Free, [], DefaultTypes(), [], [])),

        new(ScrumId, "Scrum", "Эпики, истории и подзадачи, оценка в story points на карточке.",
            new BoardBlueprint(
            [
                new("backlog", "Бэклог", StatusType.NotStarted, IsInitial: true, IsFinal: false),
                new("todo", "К работе", StatusType.NotStarted, false, false),
                new("doing", "В работе", StatusType.InProgress, false, false),
                new("review", "Проверка", StatusType.InReview, false, false),
                new("done", "Готово", StatusType.Done, false, IsFinal: true)
            ], WorkflowMode.Free, [],
            [
                new("epic", "Эпик", TaskTypeKind.Epic, false),
                new("story", "История", TaskTypeKind.Story, IsDefault: true),
                new("task", "Задача", TaskTypeKind.Task, false),
                new("subtask", "Подзадача", TaskTypeKind.Subtask, false)
            ], [],
            [
                new(null, ScreenContext.Detail,
                [
                    new("system:type"), new("system:parent"), new("system:sprint"), new("system:estimate"), new("system:priority"),
                    new("system:assignee"), new("system:due"), new("system:description"), new("system:checklist"),
                    new("system:links"), new("system:attachments")
                ])
            ])),

        new(BugTrackerId, "Баг-трекер", "Разбор ошибок: подтверждение, исправление, проверка; окружение, шаги и серьёзность.",
            new BoardBlueprint(
            [
                new("new", "Новая", StatusType.NotStarted, IsInitial: true, IsFinal: false),
                new("confirmed", "Подтверждена", StatusType.NotStarted, false, false),
                new("doing", "В работе", StatusType.InProgress, false, false),
                new("fixed", "Исправлена", StatusType.InReview, false, false),
                new("verified", "Проверена", StatusType.Done, false, IsFinal: true),
                new("rejected", "Отклонена", StatusType.Done, false, IsFinal: true)
            ], WorkflowMode.Restricted,
            [
                new("new", "confirmed", "Подтвердить"),
                new("confirmed", "doing", "Взять в работу", RequireAssignee: true),
                new("doing", "fixed", "Исправлено"),
                new("fixed", "verified", "Проверено"),
                new("fixed", "doing", "Вернуть"),
                new(null, "rejected", "Отклонить")
            ],
            [
                new("bug", "Ошибка", TaskTypeKind.Bug, IsDefault: true),
                new("task", "Задача", TaskTypeKind.Task, false)
            ],
            [
                new("environment", "Окружение", CustomFieldType.Text),
                new("steps", "Шаги воспроизведения", CustomFieldType.LongText),
                new("severity", "Серьёзность", CustomFieldType.Select, ["Блокирующая", "Критичная", "Обычная", "Незначительная"])
            ],
            [
                new(null, ScreenContext.Create,
                [
                    new("system:type"), new("system:priority"), new("custom:severity"), new("custom:environment"),
                    new("custom:steps"), new("system:description")
                ])
            ]))
    ];

    public static BuiltInBoardTemplate? Find(Guid id) => All.FirstOrDefault(t => t.Id == id);
}
