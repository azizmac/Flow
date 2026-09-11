# ТЗ: активность и комментарии задачи, Markdown-редактор

## Исходное требование

У задачи нужна лента, как в GitHub/GitLab: **комментарии** (видно, кто написал, можно отметить человека
через `@username`) и **активность** — автоматическая история изменений задачи (сменили статус, сроки,
сняли одного исполнителя и поставили другого, переименовали, добавили файлы). Описание задачи и
комментарии пишутся в **Markdown-редакторе** с переключением «Написать / Предпросмотр».

Область — все слои: Domain → Infrastructure → Application → Api → Client.

## Что уже есть и что не совпадает с требованием

- Markdown уже рендерится: `Markdig` в клиенте, `Components/MarkdownView` (HTML внутри Markdown отключён).
  Редактирование описания — голый `<textarea>` без предпросмотра и тулбара.
- Автодополнение людей уже есть на сервере: `GET /users/search?q=` (`IUserRepository.SearchAsync`, только активные).
- **Сроков у задачи нет** (`TaskItem` без `DueDate`). Чтобы в активности были «изменили срок», срок надо
  сначала добавить — включён в ТЗ отдельным пунктом (п. 8), его можно вырезать.
- **Файлов у задачи нет**, и хранилища файлов в проекте нет (нужны диск/S3, лимиты, типы, антивирус).
  Вложения — **отдельное ТЗ**; здесь только резервируем типы событий `AttachmentAdded/AttachmentRemoved`,
  чтобы лента их потом приняла без миграции модели событий.

## Принятые решения

- **Активность — append-only журнал, пишет Application, не домен.** Хендлеры команд (`TaskCreate`,
  `TaskUpdate`, `TaskAssign`, `TaskSetDueDate`, `TaskComment*`) сравнивают «до/после» и добавляют записи
  через `ITaskActivityRepository.Add` в той же `IUnitOfWork.SaveChangesAsync`. Доменные события/диспетчер
  не вводим — в проекте их нет, а ради одной ленты это лишний слой.
- **Одна запись — одно изменённое поле.** `PATCH /tasks/{id}` с новым названием и статусом даёт две записи
  `TitleChanged` + `StatusChanged`. Клиент группирует соседние записи одного актора в пределах 1 минуты
  («Илья изменил название и статус»).
- **Значения храним как строки `OldValue`/`NewValue`**, без JSON: для статуса и исполнителя — Guid, для
  срока — ISO-дата, для названия — сам текст. Клиент резолвит Guid через уже загруженные статусы проекта и
  `UserDirectory`. Удалённый статус → показываем «удалённый статус». Для `DescriptionChanged` значения не
  храним (до 4000 символов ×2 на каждую правку), только факт.
- **Комментарии — отдельная сущность `TaskComment`**, не тип активности: у них есть редактирование, тело,
  упоминания. Удаление комментария — физическое, но в активности остаётся `CommentDeleted` (без тела).
- **Упоминания разбирает сервер** (`@username` по регулярке username из `User`), резолвит в `UserId` и
  хранит в `TaskCommentMentions`. Неизвестные `@что-то` остаются текстом. Уведомлений по упоминаниям пока
  нет (нет системы уведомлений) — хранение даёт задел для «Меня упомянули» и почты.
- **Лента — два эндпоинта, склеивает клиент.** `GET /tasks/{id}/comments` и `GET /tasks/{id}/activity`
  запрашиваются параллельно, сортировка по `CreatedAt`. Один «timeline»-эндпоинт со смешанным DTO даёт
  неудобный полиморфный контракт; при объёмах Flow два запроса дешевле сложности.
- **Пагинации пока нет.** Лента задачи — десятки, не тысячи записей. Индекс `(TaskId, CreatedAt)`;
  при необходимости добавим `?after=`.
- **Права.** Комментировать — Member+ (Reader читает всё, но не пишет — так определён Reader в
  `TZ_user_roles.md`). Править — только автор. Удалять — автор или Admin+. Читать ленту и комментарии —
  все роли, как и остальные запросы.
- **Markdown-редактор — один компонент `MarkdownEditor`** для описания, нового комментария и правки
  комментария. Предпросмотр — тем же Markdig-пайплайном, что `MarkdownView` (`UseAdvancedExtensions` +
  `DisableHtml`), без похода на сервер.

## Domain (`Flow.Domain`)

### `TaskComment`
```
Id, TaskId, AuthorId, Body (Markdown, 1..10000, Trim, не пустое), CreatedAt, EditedAt?,
Mentions : IReadOnlyCollection<Guid>   // UserId, без дублей
```
- `TaskComment.Create(taskId, authorId, body, mentions)` — единственная точка создания.
- `Edit(body, mentions)` — валидирует тело, переписывает упоминания, ставит `EditedAt = UtcNow`.
  Если тело не изменилось — no-op (без `EditedAt`).
- Конструктор приватный, поля `{ get; private set; }`.

### `TaskActivity`
```
Id, TaskId, ActorId, Type : TaskActivityType, OldValue?, NewValue?, CreatedAt
```
`TaskActivityType`:

| Значение | Тип | OldValue / NewValue |
|---|---|---|
| 0 | `Created` | — / — |
| 1 | `TitleChanged` | старое / новое название |
| 2 | `DescriptionChanged` | — / — |
| 3 | `StatusChanged` | StatusId / StatusId |
| 4 | `AssigneeChanged` | UserId или null / UserId или null (null = «не назначен») |
| 5 | `DueDateChanged` | `yyyy-MM-dd` или null / `yyyy-MM-dd` или null |
| 6 | `CommentAdded` | — / CommentId |
| 7 | `CommentDeleted` | CommentId / — |
| 8 | `AttachmentAdded` | зарезервировано, отдельное ТЗ |
| 9 | `AttachmentRemoved` | зарезервировано, отдельное ТЗ |

- Фабрики на каждый тип: `TaskActivity.Created(taskId, actorId)`, `TitleChanged(taskId, actorId, old, new)` …
  Общего публичного конструктора нет, чтобы нельзя было собрать запись с несогласованным типом/значениями.
- Записи неизменяемы: методов мутации нет.

### `TaskItem` (п. 8, срок)
- `DueDate : DateOnly?`, `SetDueDate(DateOnly?)` — null снимает срок. Прошедшую дату разрешаем
  (перенос старых задач). Проверок «не раньше CreatedAt» нет.

## Infrastructure (`Flow.Infrastructure`)

| Таблица | Колонки / ограничения |
|---|---|
| `TaskComments` | PK Id; FK TaskId → TaskItems **cascade**; FK AuthorId → Users restrict (индекс); Body `text` (max 10000); CreatedAt; EditedAt null; индекс (TaskId, CreatedAt) |
| `TaskCommentMentions` | owned-коллекция `TaskComment.Mentions`; PK (CommentId, UserId); FK CommentId cascade; FK UserId → Users restrict |
| `TaskActivities` | PK Id; FK TaskId → TaskItems **cascade**; FK ActorId → Users restrict (индекс); Type int; OldValue/NewValue `text` null; CreatedAt; индекс (TaskId, CreatedAt) |
| `TaskItems` | + `DueDate date null` (п. 8) |

- Миграция `AddTaskCommentsAndActivity` (+ `AddTaskDueDate`, если п. 8 отдельно).
- Репозитории: `ITaskCommentRepository` (`GetByIdAsync`, `GetByTaskIdAsync` по CreatedAt, `Add`, `Remove`),
  `ITaskActivityRepository` (`GetByTaskIdAsync` по CreatedAt, `Add`).
- `IBoardRepository.RemoveAsync` сейчас сам догружает задачи и помечает их Deleted. Комментарии и активность
  уходят каскадом в БД — проверить интеграционным тестом, что EF не пытается севернуть связи на клиенте.
- `TaskCommentMentions` как owned-коллекция — по образцу `UserLinks`.

## Application (`Flow.Application`)

### Абстракции и права
- `ITaskCommentRepository`, `ITaskActivityRepository` (выше).
- `IPermissionService`: `EnsureCanComment(actor)` — Member+; `EnsureCanEditComment(actor, comment)` — автор;
  `EnsureCanDeleteComment(actor, comment)` — автор или Admin+. Нарушение → `ForbiddenException` → 403.
- `Features/Tasks/Mentions/MentionParser` — `Parse(body) : IReadOnlySet<string>` по регулярке
  `(?<![\w/])@([a-z0-9][a-z0-9._-]{0,30}[a-z0-9])` без учёта регистра, результат в lower; исключает
  совпадения внутри code-span/fenced-блоков (простой проход по backtick-балансу). Резолв в `UserId` — через
  `IUserRepository.GetByUsernamesAsync(names)` (новый метод, одним запросом, включая деактивированных —
  упоминание ушедшего остаётся ссылкой на профиль).

### Команды (все начинаются с `Guid ActorId`)
| Команда | Что делает | Результат |
|---|---|---|
| `TaskCommentAddCommand(ActorId, TaskId, Body)` | resolve actor → задача (404) → `EnsureCanComment` → parse mentions → `TaskComment.Create` → `TaskActivity.CommentAdded` → Save | `TaskCommentResult` (NotFound \| Success+Response) |
| `TaskCommentEditCommand(ActorId, CommentId, Body)` | комментарий (404) → `EnsureCanEditComment` → parse → `Edit` → Save. Записи активности **нет** (в ленте видно «изменено») | `TaskCommentResult` |
| `TaskCommentDeleteCommand(ActorId, CommentId)` | комментарий (404) → `EnsureCanDeleteComment` → `Remove` → `TaskActivity.CommentDeleted` → Save | `bool` |
| `TaskSetDueDateCommand(ActorId, TaskId, DateOnly? DueDate)` (п. 8) | задача → `EnsureCanEditTask` → `SetDueDate` → `DueDateChanged`, если изменилось | `TaskUpdateResult` |

### Изменения существующих хендлеров
- `TaskCreateCommandHandler` → `TaskActivity.Created`.
- `TaskUpdateCommandHandler` → `TitleChanged` / `DescriptionChanged` / `StatusChanged` только если значение
  реально изменилось (сравнение до/после; для описания — после `TrimEnd`).
- `TaskAssignCommandHandler` → `AssigneeChanged`, если исполнитель изменился.
- Пустое тело `ArgumentException` → 400 в контроллере, как везде.

### Запросы
- `TaskCommentListQuery(TaskId)` → `IReadOnlyList<TaskCommentResponse>`; несуществующая задача → null → 404.
- `TaskActivityListQuery(TaskId)` → `IReadOnlyList<TaskActivityResponse>`; аналогично.
- Маппинг — `TaskCommentMappingExtensions.ToResponse()`, `TaskActivityMappingExtensions.ToResponse()` в `Features/Tasks/`.

## Shared (`Flow.Shared/Contracts/Tasks`)

```csharp
public sealed record TaskCommentResponse(Guid Id, Guid TaskId, Guid AuthorId, string Body,
    IReadOnlyList<Guid> Mentions, DateTime CreatedAt, DateTime? EditedAt);

public sealed record CreateTaskCommentRequest(string Body);
public sealed record UpdateTaskCommentRequest(string Body);

public enum TaskActivityType { Created, TitleChanged, DescriptionChanged, StatusChanged,
    AssigneeChanged, DueDateChanged, CommentAdded, CommentDeleted, AttachmentAdded, AttachmentRemoved }

public sealed record TaskActivityResponse(Guid Id, Guid TaskId, Guid ActorId, TaskActivityType Type,
    string? OldValue, string? NewValue, DateTime CreatedAt);

public sealed record SetTaskDueDateRequest(DateOnly? DueDate);              // п. 8
// TaskResponse: + DateOnly? DueDate                                           // п. 8
```
Shared по-прежнему не ссылается на Domain: `TaskActivityType` дублируется в Shared, маппинг —
`TaskActivityMappingExtensions.ToResponseType()`.

## API (`Flow.Api`)

| Метод | Путь | Ответы |
|---|---|---|
| GET | `/tasks/{id}/comments` | 200 список; 404 |
| POST | `/tasks/{id}/comments` `{ body }` | 201 + `Location: /comments/{id}`; 400 пустое; 403 Reader; 404 |
| PATCH | `/comments/{id}` `{ body }` | 200; 400; 403 не автор; 404 |
| DELETE | `/comments/{id}` | 204; 403; 404 |
| GET | `/tasks/{id}/activity` | 200 список (по CreatedAt asc); 404 |
| PATCH | `/tasks/{id}/due-date` `{ dueDate }` (п. 8) | 200 `TaskResponse`; 403; 404 |

Новый `CommentsController` (и `/tasks/{id}/comments` в нём же, по образцу `TasksController` с явными путями).
Actor — `IActorAccessor.Require()`. Ошибки — общая схема `{ message }`.

## Client (`Flow.Client`)

### `Components/MarkdownEditor.razor`
- Параметры: `Value`/`ValueChanged`, `Placeholder`, `MinHeight`, `Busy`, `OnSubmit` (Ctrl/⌘ Enter),
  `OnCancel` (Esc), `SubmitLabel`, `Autofocus`.
- Вкладки **«Написать» / «Предпросмотр»** (как GitHub); предпросмотр — `MarkdownView` тем же пайплайном.
  Пустой текст в предпросмотре → «Нечего показывать».
- Тулбар: заголовок, жирный, курсив, зачёркнутый, код, цитата, список, нумерованный список, чек-лист, ссылка.
  Работает через `BrowserInterop` (обёртка выделения в textarea: `selectionStart/End`, `setRangeText`).
  Хоткеи `Ctrl B / I / K`. Textarea авто-растёт до `max-height`, дальше скролл.
- **`@`-автодополнение**: при вводе `@` + ≥1 символа — `Popover` от каретки со списком из
  `FlowApi.SearchUsers(q, 6)` (debounce 150 мс, `Avatar` + имя + `@username`), ↑/↓/Enter/Tab/Esc.
  Выбор вставляет `@username `.
- Стили — `tokens.css`/`app.css`, иконки — `DressyIcons` (добавить `bold`, `italic`, `strikethrough`,
  `code`, `quote`, `list`, `list-ordered`, `check-square`, `heading`, `eye`, `message`, `history`, `calendar`).
- Замещает `<textarea id="tp-desc">` в `TaskPage` (описание) и в `TaskDrawer`, если там есть редактирование описания.

### `MarkdownView` — упоминания
- После рендера Markdig `@username` (та же регулярка, что на сервере, вне `<code>`/`<pre>`) → `<a class="mention" href="/users/{username}">@username</a>`.
  Реализация — Markdig `InlineParser` для `@` (как встроенный `AutoLink`), не regex по HTML.

### Страница задачи `TaskPage` — секция «Активность»
- Под описанием. Переключатель фильтра: **Все · Комментарии · История** (по умолчанию «Все»).
- Загрузка: `GetComments(id)` + `GetActivity(id)` параллельно с задачей; слияние по `CreatedAt`; `Skeleton` до ответа.
- **Комментарий** — карточка: `Avatar` + имя (ссылка на профиль) + относительное время (`Ru`) + «изменено»
  (tooltip с датой правки) + `RowMenu` (Редактировать — автор; Удалить — автор/Admin+, через `ConfirmDialog`).
  Тело — `MarkdownView`. Правка — inline `MarkdownEditor` на месте карточки.
- **Событие активности** — компактная строка: маленький `Avatar`, текст, время. Тексты — `Services/ActivityText.cs`
  (склонения через `Ru`), примеры:
  - `Илья создал задачу`
  - `Илья изменил название: «Старое» → «Новое»`
  - `Илья обновил описание`
  - `Илья изменил статус: To Do → In Progress` (пилюли `StatusColors`)
  - `Илья назначил Азиза` / `снял Азиза` / `сменил исполнителя: Азиз → Илья` (аватары-чипы)
  - `Илья поставил срок 15 сен` / `перенёс срок: 15 сен → 20 сен` / `снял срок`
  - `Илья удалил комментарий`
  - `CommentAdded` в ленте **не показываем** — сам комментарий уже стоит в ленте, а после удаления его заменяет `CommentDeleted`. Тип нужен API-потребителям.
  - Соседние события одного актора в пределах 60 с — одна строка: `Илья изменил название и статус`, раскрывается по клику.
- **Композер** внизу: `Avatar` текущего пользователя + `MarkdownEditor` (`Ctrl Enter` — отправить).
  Виден только при `Permissions.CanComment` (Member+); Reader видит подпись «Комментировать могут участники».
  После отправки — оптимистично добавляем карточку, при ошибке откатываем и тост.
- Правка задачи из этой же страницы (статус, исполнитель, название) дописывает событие в локальную ленту
  сразу, не дожидаясь перезагрузки (`TaskResponse` вернулся → синтезируем `TaskActivityResponse` с
  `ActorId = CurrentUserId`; при следующей загрузке заменяется серверными данными).
- Якорь `#comment-{id}` для ссылок на комментарий.

### Список задач
- `TaskRow`: иконка `message` + число комментариев, если > 0. Для этого `TaskResponse.CommentCount`
  считается одним `GROUP BY TaskId` в `TaskListQuery` (`ITaskCommentRepository.CountByTaskIdsAsync`),
  по образцу `BoardResponse.TaskCount`. **Опционально**, можно отложить.

### Срок (п. 8)
- `TaskPage`/`TaskDrawer`, боковая карточка: поле «Срок» — `<input type="date">` в поповере, «Снять срок».
  `TaskRow`: чип срока; просрочено (дата < сегодня и статус не финальный) — акцент `--danger`.

### `Services/Permissions`
- `CanComment(me)` — Member+; `CanEditComment(me, comment)` — автор; `CanDeleteComment(me, comment)` — автор или Admin+.

### `Services/FlowApi`
`GetComments`, `AddComment`, `UpdateComment`, `DeleteComment`, `GetActivity`, `SetDueDate`.

## Тесты

- **Domain**: `TaskComment` — пустое/слишком длинное тело, `Edit` без изменений не ставит `EditedAt`,
  дубли в mentions схлопываются; `TaskActivity` — фабрики выставляют согласованные Type/Old/New;
  `TaskItem.SetDueDate` (п. 8).
- **Application** (`Fakes/FakeTaskCommentRepository`, `FakeTaskActivityRepository`, обновить `TestMediatorFactory`):
  `TaskUpdate` пишет по записи на каждое изменённое поле и ничего при том же значении; `TaskAssign` →
  `AssigneeChanged`; `TaskCreate` → `Created`; `MentionParser` — регистр, знаки препинания, `@` в code-span,
  e-mail (`a@b.com` не упоминание); комментарий Reader → 403; правка чужого → 403; удаление чужого Admin → ок,
  Developer → 403; удаление пишет `CommentDeleted`. `PermissionTests` — по тесту на новую строку матрицы.
- **Infrastructure**: миграция; удаление доски с задачами, у которых есть комментарии/активность/упоминания;
  порядок `GetByTaskIdAsync`; `GetByUsernamesAsync` одним запросом.
- **Api**: `POST /tasks/{id}/comments` → 201 + Location; 403 для Reader; `GET /tasks/{id}/activity` после
  PATCH задачи содержит `StatusChanged`.

## Порядок работ (PR по слоям, как в #8)

1. Domain + Infrastructure: `TaskComment`, `TaskActivity`, конфигурации, миграция, репозитории, тесты.
2. Application + Api — активность: запись в существующих хендлерах, `TaskActivityListQuery`, `GET /tasks/{id}/activity`.
3. Application + Api — комментарии и упоминания: команды, `MentionParser`, `GetByUsernamesAsync`, права, `CommentsController`.
4. Client — `MarkdownEditor` (вкладки, тулбар, `@`-автодополнение), замена textarea описания, упоминания в `MarkdownView`.
5. Client — лента «Активность» на странице задачи, композер, `ActivityText`, `Permissions`.
6. Срок задачи (п. 8) сквозь все слои — отдельный PR, можно параллельно с 4–5.
7. Опционально: `CommentCount` в списке задач.

Обновить `AGENTS.md` (таблица API, СБД, права) и `README`.

## Вне области

- Вложения/файлы (отдельное ТЗ: хранилище, лимиты, `AttachmentAdded/Removed`, drag-and-drop в редактор).
- Уведомления по упоминаниям (почта, колокольчик) и страница «Меня упомянули».
- Реакции, ответы в тред, цитирование.
- Активность на уровне проекта/пользователя (сквозная лента «что произошло за день»).
- Realtime (SignalR) — лента обновляется при перезагрузке страницы и после собственных действий.
- Активность по переименованию/удалению проекта — не относится к задаче.

## Что сделано и отличия от плана

Реализовано целиком (ветка `claude/hopeful-fermat-jm7fbo`), включая п. 8 (срок) и п. 7 (`CommentCount`). Отличия:

- `@`-автодополнение в редакторе берёт людей из уже загруженного `UserDirectory` (как `AssigneeSelect`), а не из `GET /users/search` — без сетевых запросов на каждую букву.
- Ссылка упоминания ведёт на `/u/{username}` (страница-редирект на `/users/{id}`), потому что профиль адресуется по Id.
- Список автодополнения — не `Popover` (тот забирает фокус у textarea), а слой под полем ввода; перехват клавиш — в `flow.js` по `data-mention`.
- Тексты журнала без глаголов прошедшего времени («задача создана», «статус: A → B»): род актора неизвестен.
- Ctrl Enter в описании обрабатывает сам редактор; страничные хоткеи это сочетание глушат, чтобы не сохранить дважды.

## Открытые вопросы

1. Reader комментирует или только читает? В ТЗ — только читает (Member+).
2. Срок задачи (п. 8) делаем сейчас или это отдельная задача?
3. Число комментариев в списке задач (п. 7) нужно?
4. `DescriptionChanged` без хранения старого текста устраивает, или нужна история версий описания?
