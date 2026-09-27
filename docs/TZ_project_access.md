# ТЗ: права на уровне проекта — участники, приватные проекты, группы, наборы прав

Статус: **этапы 4A–4B сделаны** (роли в проекте, участники, роль по умолчанию, приватные проекты с фильтром
видимости во всех запросах чтения, поиске, похожих задачах и файлах), 4C–4E — не начаты. Видимость меняется
`PUT /boards/{id}/visibility`, а не общим `PATCH /access`; в приватном проекте роль по умолчанию не действует. Часть плана `docs/TZ_roadmap_jira_parity.md` (блок 4). Развивает
`docs/TZ_user_roles.md`: там глобальные роли прямо названы временной мерой «до закрытых проектов».

## Исходное требование

Роли на уровне проекта, группы, команды, наборы прав (permission sets). Сейчас у пользователя одна глобальная роль,
приватных проектов нет.

## Как устроено сейчас

- `User.Role : UserRole` (Reader … Owner) — одна на всё. `PermissionService.Ensure*` принимает `User actor` и,
  где нужно, задачу или комментарий, но не проект.
- **Запросы actor не получают**: читать может любая роль, всё. Исключение — `SearchQuery`: он получает `ActorId`,
  а `BoardId` чанка заранее заложен как точка фильтрации приватных проектов.
- `/files/{id}` отдаёт вложение любому вошедшему: «проверки прав на чтение вложения в Flow нет и не было»
  (`AGENTS.md`, раздел «Вложения»).
- Интерфейс держит зеркало матрицы — `Services/Permissions` на Shared-enum'ах.

## Принятые решения

- **Две оси: глобальная роль и роль в проекте.** Глобальная роль (`UserRole`) отвечает за workspace: люди,
  создание проектов, поиск-диагностика. Роль в проекте (`ProjectRole`) — за работу внутри проекта.
- **Лестница ролей проекта фиксирована:** Viewer < Member < Developer < Admin. Она зеркалит нижнюю часть
  глобальной лестницы, и текущая матрица задач переносится один в один.
- **Наборы прав вводятся как слой под ролями, а не вместо них.** Внутри кода проверка идёт по
  `ProjectPermission` (enum прав), роль — это именованный набор прав. Первая версия — наборы зашиты в коде
  (`ProjectRoleDefaults`). Редактируемые наборы на workspace (как `permission_sets` Windshift) — этап 4E:
  после 4A–4D это правка таблицы, а не переписывание проверок.
- **Приватность — свойство проекта:** `Board.Visibility = Open | Private`. Open-проект видят все по
  глобальной роли, как сейчас. Private видят только участники и глобальные Admin+.
- **Эффективная роль = максимум** из «по умолчанию для Open-проекта» (производная от глобальной),
  прямого участия и участия через группы. Запретов («deny») нет: запреты в сочетании с группами дают матрицы,
  которые никто не может предсказать.
- **Невидимое — 404, а не 403.** Код задачи из приватного проекта в ссылке не должен подтверждать, что такая
  задача существует. 403 остаётся для «вижу, но не могу изменить».
- **Справочник людей остаётся общим.** Пользователи, аватары и `@`-автодополнение видны всем, как сейчас:
  люди — сущность workspace, а не проекта. Скрываются задачи, комментарии, вложения, журнал, фильтры по ним.
- **Миграция без изменения поведения.** Все существующие проекты — `Open`, участников нет, эффективная роль
  выводится из глобальной. После выката каждый видит и может ровно то же, что и до.

## 1. Модель

```
ProjectRole : Viewer=0, Member=1, Developer=2, Admin=3            // порядок = сила
Board.Visibility : Open=0 | Private=1
Board.DefaultRole : ProjectRole?                                   // для Open; null — производная от глобальной
BoardMember: (BoardId, UserId) PK, Role : ProjectRole, AddedById, AddedAt
Group: Id, Name (уникально), Description?, IsTeam : bool, CreatedAt
GroupMember: (GroupId, UserId) PK, AddedAt
BoardGroup: (BoardId, GroupId) PK, Role : ProjectRole
```

Производная роль для Open-проекта:

| Глобальная `UserRole` | Роль в Open-проекте | Роль в Private-проекте без участия |
|---|---|---|
| Reader | Viewer | — (не видит) |
| Member | Member | — |
| Developer | Developer | — |
| Admin, Owner | Admin | Admin (видит все проекты) |

`Board.DefaultRole` позволяет сделать Open-проект «только чтение для всех, кроме участников»
(`DefaultRole = Viewer`). Производная роль тогда — `min(глобальная, DefaultRole)`, кроме Admin+.

### Команды (`IsTeam`)

Группа с `IsTeam = true` — это команда: её можно указать в задаче (`TaskItem.TeamId : Guid?`, FK SetNull),
фильтровать по ней (`team = …` в FQL) и выводить дорожкой на канбане. Отдельной сущности «команда» с аватаром
и цветом, как в Windshift, не заводим: разница с группой — один флаг.

## 2. Права проекта (`ProjectPermission`)

| Право | Viewer | Member | Developer | Admin |
|---|---|---|---|---|
| `ViewProject` (задачи, комментарии, вложения, журнал) | ✓ | ✓ | ✓ | ✓ |
| `CreateTask` | — | ✓ | ✓ | ✓ |
| `EditOwnTask` (создатель или исполнитель) | — | ✓ | ✓ | ✓ |
| `EditAnyTask`, `DeleteTask`, `AssignAnyone` | — | — | ✓ | ✓ |
| `Comment`, `Attach` | — | ✓ | ✓ | ✓ |
| `DeleteAnyComment`, `DeleteAnyAttachment` | — | — | — | ✓ |
| `ManageSprints`, `ManageMilestones` | — | — | ✓ | ✓ |
| `ManageConfig` (статусы, workflow, типы, поля, экраны) | — | — | — | ✓ |
| `ManageMembers` | — | — | — | ✓ |
| `ManageScm` (привязка репозиториев) | — | — | — | ✓ |
| `RenameProject`, `DeleteProject` | — | — | — | ✓ (удаление — ещё и глобальный Admin+) |

Глобальные права (`UserRole`) остаются как есть: люди и роли, создание проектов (Admin+), диагностика поиска,
переиндексация.

## 3. Проверки

### `IProjectAccess`

```csharp
public interface IProjectAccess
{
    Task<ProjectRole?> RoleInAsync(Guid actorId, Guid boardId, CancellationToken ct); // null — не видит
    Task<IReadOnlyCollection<Guid>?> VisibleBoardIdsAsync(Guid actorId, CancellationToken ct); // null — видит все
    Task EnsureAsync(Guid actorId, Guid boardId, ProjectPermission permission, CancellationToken ct);
}
```

- Реализация в Infrastructure: один запрос за роли actor во всех проектах (прямые + через группы) и кэш на время
  scope. Для Blazor-circuit — на время запроса `IFlowApi`, не на весь circuit: снятие участника должно
  действовать сразу.
- `VisibleBoardIdsAsync` возвращает `null` для глобальных Admin+ и когда приватных проектов нет вовсе.
  Иначе — `Open ∪ участие`. Все запросы добавляют `WHERE BoardId = ANY(@visible)` только когда список не `null`:
  установка без приватных проектов не платит за фильтр.
- `PermissionService.Ensure*` для задач, комментариев и вложений переписываются поверх `ProjectPermission`
  с ролью в проекте задачи. Сигнатуры команд не меняются: у всех изменяющих команд уже есть `ActorId`.

### Запросы получают `ActorId`

Каждый запрос, который отдаёт данные проекта, начинает принимать `ActorId`: `BoardListQuery`, `BoardGetQuery`,
`TaskListQuery`, `TaskSearchQuery`, `TaskGetQuery`, `TaskCommentListQuery`, `TaskActivityListQuery`,
`AttachmentListQuery`, `AttachmentMetaQuery`, `AttachmentContentQuery`, `SimilarTasksQuery`, все новые запросы
из `TZ_task_model` и `TZ_task_views` (дерево, канбан, FQL, агрегаты виджетов, отчёты спринтов, вехи),
`SearchQuery` (фильтр по `BoardId` чанка в обеих CTE — уже подготовлено).

**Отдельно проверить:**

- `/files/{id}` и `/api/attachments/{id}/content` — появляется проверка чтения (404 для невидимого). ETag
  и `Cache-Control: private, no-cache` уже стоят, поэтому отнятый доступ действует сразу, без протухания кэша.
- `SimilarTasksQuery` ищет по всей базе. Фильтр по видимым проектам должен стоять **внутри** HNSW-запроса,
  иначе окно `k` заполнится невидимыми задачами и выдача опустеет (та же ловушка, что закрыта
  `hnsw.iterative_scan` в основном поиске).
- Связи задач (`TZ_task_model` §5): вторая задача невидима → `restricted: true` без названия.
- Упоминания в комментариях: `@username` пользователя без доступа к проекту по-прежнему резолвится (справочник
  общий), но уведомлений нет — и не будет, пока проект ему невидим.
- Журнал: `OldValue`/`NewValue` при переносе задачи между проектами (`TZ_task_model` §6) содержат коды — видимые
  только тем, кто видит задачу. Отдельной фильтрации не нужно.
- `BoardDelete` — глобальный Admin+ **и** `DeleteProject`.
- Сохранённые фильтры и дашборды: считаются правами смотрящего (`TZ_task_views` §7–8).

## 4. API

| Метод | Путь | Кто |
|---|---|---|
| GET | `/boards/{id}/members` — прямые участники и группы с ролями, плюс вычисленная роль actor | `ViewProject` |
| PUT/DELETE | `/boards/{id}/members/{userId}` `{ role }` | `ManageMembers` |
| PUT/DELETE | `/boards/{id}/groups/{groupId}` `{ role }` | `ManageMembers` |
| PATCH | `/boards/{id}/access` `{ visibility, defaultRole }` | `ManageMembers` |

> **Сделано в 4A иначе:** роль по умолчанию — `PUT /boards/{id}/default-role { role }` (одно поле, null снимает:
> в PATCH-семантике null означал бы «не трогать»). Видимость добавит свой эндпоинт в 4B. Список участников для
> экрана «Доступ» — `GET /boards/{id}/members`; `DeleteTask` отдельным правом не стал: удаление задачи, как и раньше,
> — это правка (Member удаляет свою). Права `ManageSprints`/`ManageMilestones`/`ManageScm` появятся вместе с
> сущностями, которые защищают. Роль в проекте не кэшируется на запрос: время жизни scope не всегда равно запросу.
| GET | `/boards/{id}/my-access` — роль и список `ProjectPermission` для клиента | `ViewProject` |
| GET/POST | `/groups`; GET/PATCH/DELETE `/groups/{id}`; PUT/DELETE `/groups/{id}/members/{userId}` | чтение — все, изменение — глобальный Admin+ |

Инварианты:

- Admin проекта не выдаёт роль выше своей и не понижает себя, если он последний Admin приватного проекта:
  тот же приём, что «последний Owner» в `UserChangeRoleCommandHandler`. Глобальный Admin+ проект всё равно
  видит, но терять единственного локального администратора не нужно.
- Перевод проекта в `Private`, когда у проекта нет ни одного участника, кроме глобальных Admin+, — предупреждение
  в диалоге, не ошибка.
- Деактивированный пользователь остаётся в `BoardMember` (история), но прав не имеет: `ActorResolver` отсекает
  его раньше.

## 5. Клиент

- `Services/Permissions` получает роль в проекте: `Permissions.For(board)` → набор `ProjectPermission` из
  `GET /boards/{id}/my-access` (кэш на страницу). Проверки `CanEditTask`/`CanAssign` в `Tasks.razor`, `TaskDrawer`,
  `BoardCard` переходят на него. Сервер — источник истины, 403 → тост, как и сейчас.
- Режим «Все проекты»: права считаются по проекту каждой строки. `my-access` для всех видимых проектов
  грузится одним запросом `GET /boards/my-access`.
- Настройки проекта (`/boards/{id}/settings`), вкладка «Доступ»: видимость, роль по умолчанию, список участников
  (`AssigneeSelect`-подобный выбор человека + `RoleSelect` с ролями проекта), группы.
- Экран «Люди» получает вкладку «Группы» (Admin+). Профиль человека — список его проектов с ролями.
- Замок у приватного проекта в `BoardCard`, `BoardSelect`, в чипе проекта в выдаче поиска.

## 6. Миграция

`AddProjectAccess`: таблицы `BoardMembers`, `Groups`, `GroupMembers`, `BoardGroups`, колонки
`Boards.Visibility = 0`, `Boards.DefaultRole = null`, `TaskItems.TeamId`. Данные не переносятся: производная
роль даёт прежнее поведение. Индексы: `BoardMembers(UserId)`, `GroupMembers(UserId)`, `BoardGroups(GroupId)`.

## 7. Наборы прав (этап 4E)

- `PermissionSet: Id, Name, IsBuiltIn, Permissions : ProjectPermission[]`. Встроенные — четыре роли из
  таблицы §2, их нельзя удалить, но можно клонировать.
- `BoardMember.Role` и `BoardGroup.Role` превращаются в ссылку на набор (`PermissionSetId`). Лестница `ProjectRole`
  остаётся для сравнения «не выше своей» по встроенному предку набора.
- Редактор — матрица «набор × право» на экране настроек workspace (Owner).
- Зеркало на клиенте (`Services/Permissions`) после этого перестаёт быть статическим и берёт набор с сервера
  (`my-access` уже возвращает список прав, поэтому клиент к 4E готов с этапа 4A).

## Тесты

- Application: `PermissionTests` — по тесту на каждую строку таблицы §2 и §4 (как сейчас на каждую строку
  `TZ_user_roles`); максимум ролей (прямая + две группы); `DefaultRole` у Open-проекта; последний Admin
  приватного проекта; 404 вместо 403 для невидимого (на каждом запросе из списка §3 — параметризованный тест).
- Infrastructure: `VisibleBoardIdsAsync` (группы, Open/Private, глобальный Admin); фильтр видимости в SQL поиска
  (обе CTE) и в `FindSimilarAsync` — невидимые не съедают окно `k`; миграция не меняет видимость существующих
  проектов.
- Api: `/files/{id}` на невидимое вложение → 404; `GET /boards/my-access`.

## Этапы

| Этап | Состав |
|---|---|
| 4A | `ProjectRole`, `BoardMember`, `IProjectAccess`, перевод `PermissionService` на права проекта, `my-access`, экран «Доступ» (без приватности — роль по умолчанию и участники) |
| 4B | `Visibility = Private`, `ActorId` во всех запросах, фильтры в поиске, похожих и вложениях |
| 4C | Группы и роли групп в проекте |
| 4D | Команды (`IsTeam`, `TaskItem.TeamId`) |
| 4E | Редактируемые наборы прав |

**Рекомендуемый порядок в общем плане: 4A–4B раньше представлений** (`TZ_task_views`). Каждый новый запрос
(дерево, канбан, FQL, виджеты) иначе придётся потом дорабатывать под фильтр видимости, а пропущенный запрос —
это утечка данных приватного проекта.

## Вне объёма

Права на уровне отдельной задачи (security level в Jira), запреты (deny), гостевой доступ извне workspace,
синхронизация групп с LDAP/SCIM, несколько workspace.
