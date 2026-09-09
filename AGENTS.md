# Flow — трекер задач по проектам (C#, .NET 10)

## Слойная архитектура
```
Flow.Api           → контроллеры (только IMediator)
Flow.Shared        → DTO-контракты (Boards, Tasks, Users) — общий
Flow.Application   → Features/{Boards,Tasks,Users}/{Commands,Queries}/*, Abstractions (IBoardRepository, ITaskItemRepository, IUserRepository, IUnitOfWork)
Flow.Domain        → сущности Board, Status, TaskItem, TaskCode, StatusType, DefaultStatuses, User, UserLink, UserLinkType
Flow.Infrastructure→ EF Core (Postgres/Npgsql), репозитории, UnitOfWork, миграции
Flow.Client        → Blazor WebAssembly: экраны «Проекты» (/boards), «Задачи проекта» (/boards/{id}), задача (/tasks/{id}), «Люди» (/users), профиль (/users/{id}); ходит в Flow.Api через Services/FlowApi
```
Зависимости: Domain ← Application ← Infrastructure ← Api.
Shared намеренно **не ссылается** на Domain (свои enum `StatusType`, `UserLinkType`).

## Ключевые инварианты Board (aggregate root)
- `Key`: `^[A-Z][A-Z0-9]{1,9}$`, префикс для TaskCode.
- `Create(name, key)` — единственная публичная точка создания; сажает 4 статуса из `DefaultStatuses`.
- Максимум 1 начальный и 1 финальный статус на доску (проверяется в `AddStatus`/`SetInitialStatus`).
- `CreateTask`: без `statusId` → в начальный; `NextTaskNumber++` → TaskCode; **новые задачи регистрируются через `ITaskItemRepository.Add`**, не через коллекцию `Board.Tasks`.
- Все поля `{ get; private set; }`, мутации только через методы.

## User (профиль, не учётная запись)
- `User.Create(username, email, firstName, lastName)` — единственная точка создания; username/email нормализуются в lower, username `^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$`.
- Внешние ссылки — коллекция `UserLink` (`SetLink`/`RemoveLink`), не более одной на `UserLinkType`; телефон хранится в E.164 (`+79991234567`).
- Удаления нет: `Deactivate()`/`Activate()` (`IsActive`, `DeactivatedAt`). Пароли/роли в домене отсутствуют намеренно.
- Уникальность username/email: `UserCreate` → `UserCreateResult` (`IsUsernameTaken`/`IsEmailTaken` → 409); `UserChangeUsername`/`UserChangeEmail` → `UserUpdateResult.ConflictError` → 409; плюс unique-индексы в БД.
- Все изменяющие команды пользователя возвращают общий `Features/Users/UserUpdateResult` (NotFound | ConflictError | Success). `UserUpdateProfile` — PATCH-семантика: null не трогать, пустая строка очищает.
- Назначение на задачу: `TaskItem.AssigneeId : Guid?`, `Assign`/`Unassign`; **назначать можно только активного пользователя** — проверяет `TaskAssignCommandHandler` (`TaskAssignResult`: NotFound | ValidationError | Success). `TaskListQuery(boardId, assigneeId?)` фильтрует по исполнителю.
- `IUserRepository.SearchAsync` — автодополнение для `@` (ILIKE по Username/FirstName/LastName, только активные, `%`/`_` экранируются).
- План по слоям: GitHub #8 (подзадачи #9–#13), ТЗ — `docs/TZ_user.md` (там же чек-лист перевода на `UserId` после мержа ветки `StronglyTypeId`).

## MediatR и DI
- `AddFlowApplication()` регистрирует MediatR с reflection-сканированием сущности `Flow.Application` — хендлеры `internal`, commands/queries `public sealed record`.
- `AddFlowInfrastructure(config)` регистрирует `FlowDbContext` (UseNpgsql) + scoped-репозитории + `IUnitOfWork`.
- `IUnitOfWork.SaveChangesAsync` — единственный способ коммитить.

## API (MVC, `[ApiController]`)
| Метод | Путь |
|---|---|
| POST/GET | `/boards` |
| GET/PATCH/DELETE | `/boards/{id}`, `/boards/{id}/name` |
| POST/GET | `/boards/{boardId}/tasks` (`?assigneeId=`) |
| GET/PATCH/DELETE | `/tasks/{id}`, PATCH `/tasks/{id}/assignee` |
| POST/GET | `/users`, `/users/search?q=&limit=` |
| GET/PATCH | `/users/{id}`, `/users/by-username/{username}`, `/users/{id}/username`, `/users/{id}/email` |
| PUT/DELETE | `/users/{id}/links/{type}` |
| POST | `/users/{id}/deactivate`, `/users/{id}/activate` |

Обработка ошибок: `catch (ArgumentException or InvalidOperationException)` → 400.
`TaskUpdate` возвращает `TaskUpdateResult` (NotFound | InvalidStatus | Success+Response).
`BoardResponse` несёт `TaskCount` и `NextTaskNumber` (счётчик задач — один `GROUP BY` через `ITaskItemRepository.CountByBoardIdsAsync`, не N+1); `TaskResponse` несёт `BoardId`.
`BoardCreate` возвращает `BoardCreateResult` (KeyTaken → 409 | Success+Response); ключ проверяется через `IBoardRepository.ExistsByKeyAsync` по нормализованному `Board.Key`.
`BoardDelete` → `IBoardRepository.RemoveAsync`: репозиторий сам догружает задачи и помечает их Deleted до доски, иначе EF упрётся в FK Restrict (`TaskItems.StatusId`) — либо в БД (23503), либо на клиенте (severed required relationship).

## СБД (Postgres 16, `flow/flow/flow`, :5432)
- `Boards`: PK Id, unique Key
- `Statuses`: PK Id, FK BoardId (cascade), unique (BoardId, SortOrder) и (BoardId, Name)
- `TaskItems`: PK Id, FK BoardId (cascade), FK StatusId (restrict), FK AssigneeId → Users (restrict, nullable, индекс), unique Code
- `Users`: PK Id, unique Username, unique Email (значения уже lower — индексы регистронезависимы по факту)
- `UserLinks`: owned-коллекция `User.Links`, PK (UserId, Type), FK UserId (cascade)
- Стр-подк: `ConnectionStrings:Postgres` (appsettings для локалки, env `ConnectionStrings__Postgres` в docker).

## Команды
- Требуется .NET SDK 10.x (закреплено в `global.json`, `rollForward: latestMinor`); SDK 8/9 сборку не соберут (NETSDK1045).
- Build/test: `dotnet build Flow.slnx`, `dotnet test Flow.slnx`
- `Flow.Infrastructure.Tests` поднимает Postgres через Testcontainers — нужен запущенный Docker.
- Migrations: `dotnet ef database update --project src/Flow.Infrastructure --startup-project src/Flow.Api`
- Docker: `docker compose up -d postgres`
- Клиент локально: `dotnet run --project src/Flow.Api` (:5000) + `dotnet run --project src/Flow.Client` (:5016); клиент читает `ApiBaseUrl` из `wwwroot/appsettings.json`.
- CI: GitHub Actions — `ci.yml` (триггеры и порядок) вызывает `build.yml` (restore → build Release → юнит-тесты → интеграционные на Testcontainers) и `docker.yml` (образ `Flow.Api`; с `main` публикуется в registry по секретам `REGISTRY_USERNAME`/`REGISTRY_PASSWORD` и переменным `REGISTRY`/`IMAGE_NAME`).

## Правила стиля (унаследованы)
- Конструктор сущностей — приватный; фабрики `Board.Create`, `TaskCode.Create`.
- Методы фич живут в `Features/<Entity>/<Commands|Queries>/<Name>/<Name>.cs` + `<Name>Handler.cs`.
- Маппинг → `*MappingExtensions.ToResponse()` в `Features/<Entity>/`.
- Доменные ошибки → `ArgumentException` / `InvalidOperationException` (не `DomainException`, не `Result`).
- `TaskCode` — value object, в БД хранится как string (EF value converter).
- `Status.SortOrder`: автоинкремент при добавлении, не редактируется, только для `ORDER BY`.
- Blazor Client: Board в UI называется «проект», задачи — плоский список со статусом и исполнителем в строке (не канбан). DTO только из Flow.Shared, ничего не дублировать. Стили — DRESSY-токены (`wwwroot/css/tokens.css`) + классы из макета (`app.css`); иконки — DRESSY-глифы в `Components/DressyIcons.cs` (плюс контурные `user`, `user-plus`, `user-off`, `at`, `mail`, `phone`, `link`, `briefcase`, `check-circle`, `external` для раздела «Люди»). Даты/склонения/username-транслит — `Services/Ru.cs` (InvariantGlobalization включён). Адрес API — `wwwroot/appsettings.json` → `ApiBaseUrl`; в `Flow.Api` CORS-origins клиента — `Cors:Origins`.
- Blazor Client, пользователи: справочник `Services/UserDirectory` (один `GET /users?includeInactive=true` на сессию, `Find(id)`, `Put(user)` после изменений, событие `Changed`) — строки задач берут аватар исполнителя из него, а не через `GET /users/{id}`. Аватар — `Components/Avatar` (картинка или инициалы на тинте по Id, `Services/Avatars`). Выбор исполнителя — `AssigneeSelect` (поповер с локальным фильтром по активным), фильтр списка — `AssigneeFilter` (`?who=<guid>|none`), карточка по клику — `UserCard`. «Это я» (профиль в футере сайдбара) — `AppState.CurrentUserId` ↔ localStorage `flow.me`, аутентификации в API нет. Макеты: артефакт Claude Design «Flow CRM» (Profile.dc.html → страница профиля), перенесены в тёмную тему Flow.
- Motion (по emilkowalski/skills, `emil-design-eng`): анимируем только transform/opacity, кривые из `tokens.css` (`--ease-out`, `--ease-drawer`), UI ≤ 300ms, press-feedback `scale(.97)`, поповеры от якоря (`transform-origin`), hover только под `(hover: hover) and (pointer: fine)`, `prefers-reduced-motion` снимает transform-движение. Хоткеи (N, Esc, ↑/↓) — без анимаций.

## Тесты (xUnit)
- `Flow.Domain.Tests` — юнит-тесты сущностей (Board, TaskItem, TaskCode, User).
- `Flow.Application.Tests` — фичи с `Fakes/` (FakeBoardRepository, FakeTaskItemRepository, FakeUserRepository, FakeUnitOfWork) + `TestMediatorFactory`.
- `Flow.Infrastructure.Tests` — интеграционные на реальном Postgres (Testcontainers, образ `postgres:16-alpine`): миграции, удаление доски с задачами, дубликат ключа, пользователи (ссылки, unique-индексы, поиск).

## Навигация
- Структура проекта: `docs/Struktura_board_task_status.md`
- Сравнение подходов DbContext: `docs/Sravnenie_DbContext_podhodov.md`
- ТЗ: `docs/TZ_board_task_status.md`, `docs/TZ_user.md`
