namespace Flow.Client.Data;

/// <summary>Static sample content mirroring the design handoff's renderVals() fixtures.</summary>
public static class SampleData
{
    public static readonly List<TaskItem> HomeTasks =
    [
        new("Ревью PR #482", "Frontend", "сегодня, 15:00", "#FFBA00", "ИВ"),
        new("Обновить CI pipeline", "DevOps", "сегодня, 17:00", "#6D9773", "АК"),
        new("Написать unit-тесты для API", "Backend", "завтра", "#BB8A52", "МС"),
        new("Митинг с клиентом", "Product", "сегодня, 12:00", "#E2604F", "ИВ"),
        new("Задеплоить релиз 2.4", "DevOps", "завтра", "#6D9773", "АК"),
    ];

    public static readonly List<TaskItem> Tasks =
    [
        new("Ревью PR #482", "Frontend", "24 авг", "#FFBA00", "ИВ", 4, "2/3"),
        new("Обновить CI pipeline", "DevOps", "24 авг", "#6D9773", "АК", 1, "3/5"),
        new("Настроить SSO для воркспейса", "DevOps", "28 авг", "#E2604F", "ИВ", 3, "2/4"),
        new("Написать unit-тесты для API", "Backend", "25 авг", "#BB8A52", "МС", 0, "0/6"),
        new("Митинг с клиентом", "Product", "24 авг", "#E2604F", "ЛП", 2, "1/1"),
        new("Задеплоить релиз 2.4", "DevOps", "25 авг", "#6D9773", "АК", 0, "4/4"),
        new("Дизайн онбординга v2", "Design", "29 авг", "#BB8A52", "ЛП", 6, "1/5"),
    ];

    public static readonly List<ChecklistEntry> Checklist =
    [
        new("Настроить Google OAuth", true),
        new("Настроить GitHub OAuth", true),
        new("Маппинг ролей по домену", false),
        new("Протестировать вход", false),
    ];

    public static readonly List<CommentItem> Comments =
    [
        new("Алексей Крылов", "АК", "GitHub OAuth готов, жду ревью маппинга ролей."),
        new("Ирина Волкова", "ИВ", "Посмотрю сегодня до конца дня."),
        new("Мария Седова", "МС", "Нужно согласовать с безопасностью."),
    ];

    public static readonly List<TeamSummary> Teams =
    [
        new("Frontend", 5, ["ИВ", "АК", "ЛП"], 68, 9),
        new("Backend", 6, ["МС", "ДТ", "НК"], 82, 14),
        new("DevOps", 3, ["АК", "ГР"], 54, 5),
        new("Product Design", 4, ["ЛП", "ИВ"], 40, 6),
        new("QA", 4, ["НК", "МС"], 61, 8),
        new("Growth", 3, ["ГР", "ДТ"], 30, 3),
    ];

    public static readonly List<TeamMember> Members =
    [
        new("Ирина Волкова", "Lead Product Designer", "ИВ"),
        new("Алексей Крылов", "Senior Frontend", "АК"),
        new("Лия Панина", "Frontend", "ЛП"),
        new("Данил Титов", "Frontend", "ДТ"),
        new("Настя Круз", "UI/UX Designer", "НК"),
    ];

    public static readonly List<TeamTaskDetail> TeamTasksBig =
    [
        new("Ревью PR #482", "24 авг", "#FFBA00", "PR #482 · 3 файла изменено", "Алексей Крылов", "АК"),
        new("Дизайн онбординга v2", "29 авг", "#BB8A52", null, "Лия Панина", "ЛП"),
        new("Обновить дизайн-систему", "30 авг", "#6D9773", "PR #491 · на ревью", "Ирина Волкова", "ИВ"),
        new("Фикс адаптива дашборда", "26 авг", "#E2604F", null, "Данил Титов", "ДТ"),
    ];

    public static readonly List<ActivityItem> Activity =
    [
        new("Алексей Крылов", "АК", "закрыл задачу «Обновить CI pipeline»", "10 мин назад"),
        new("Мария Седова", "МС", "оставила комментарий в «SSO для воркспейса»", "32 мин назад"),
        new("Лия Панина", "ЛП", "создала задачу «Дизайн онбординга v2»", "1 ч назад"),
        new("Данил Титов", "ДТ", "взял в работу «Фикс адаптива дашборда»", "2 ч назад"),
    ];

    public static readonly List<StatusBreakdown> StatusBreakdownList =
    [
        new("К выполнению", 12, 25, "#BB8A52"),
        new("В работе", 16, 33, "#FFBA00"),
        new("На проверке", 6, 12, "#6D9773"),
        new("Готово", 34, 70, "#6D9773"),
        new("Просрочено", 5, 10, "#E2604F"),
        new("Отложено", 3, 6, "rgba(255,255,255,0.3)"),
    ];

    public static readonly List<Deadline> Deadlines =
    [
        new("Настроить SSO для воркспейса", "28 авг", "#E2604F"),
        new("Дизайн онбординга v2", "29 авг", "#BB8A52"),
        new("Задеплоить релиз 2.4", "25 авг", "#6D9773"),
        new("Написать unit-тесты для API", "25 авг", "#BB8A52"),
    ];

    public static readonly (string Label, bool Active)[] TaskStatuses =
    [
        ("К выполнению", true),
        ("В работе", true),
        ("На проверке", false),
        ("Готово", false),
        ("Просрочено", false),
        ("Отложено", false),
    ];

    public static readonly string[] PeriodOptions = ["Неделя", "Месяц", "Квартал"];

    private static readonly (string Label, string Icon, string Slug)[] SettingsNavRaw =
    [
        ("Профиль", "user", "profile"),
        ("Уведомления", "message", "notifications"),
        ("Тема оформления", "palette", "theme"),
        ("Язык", "browser", "language"),
        ("Безопасность", "lock", "security"),
        ("Интеграции", "desktop", "integrations"),
        ("Участники и роли", "users-add", "members"),
    ];

    public static List<SettingsNavEntry> SettingsNav() =>
        SettingsNavRaw.Select(s => new SettingsNavEntry(s.Label, s.Icon, $"/settings/{s.Slug}")).ToList();

    public static readonly List<IntegrationEntry> Integrations =
    [
        new("GitHub", "Синхронизация PR и коммитов с задачами", "browser", "Подключено", "secondary"),
        new("Slack", "Уведомления о задачах в канал команды", "message", "Подключить", "primary"),
        new("Google Calendar", "Дедлайны задач в календаре", "calendar", "Подключить", "primary"),
    ];

    public static readonly List<MdLine> MdLines =
    [
        new(1, "# ТЗ: SSO для воркспейса", "#FFBA00"),
        new(2, "", "#fff"),
        new(3, "## Цель", "#FFBA00"),
        new(4, "Добавить единый вход (SSO)", "rgba(255,255,255,0.68)"),
        new(5, "через Google и GitHub.", "rgba(255,255,255,0.68)"),
        new(6, "", "#fff"),
        new(7, "## Требования", "#FFBA00"),
        new(8, "- OAuth 2.0 Google/GitHub", "rgba(255,255,255,0.68)"),
        new(9, "- Маппинг ролей по домену", "rgba(255,255,255,0.68)"),
        new(10, "- Фолбэк на email/пароль", "rgba(255,255,255,0.68)"),
        new(11, "", "#fff"),
        new(12, "## Критерии приёмки", "#FFBA00"),
        new(13, "- [x] Вход через Google/GitHub", "#6D9773"),
        new(14, "- [ ] Роль по домену почты", "rgba(255,255,255,0.68)"),
    ];
}
