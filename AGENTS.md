# Flow — Kanban-приложение (C#, .NET 10)

## Слойная архитектура
```
Flow.Api           → контроллеры (только IMediator)
Flow.Shared        → DTO-контракты (Boards, Tasks) + типизированные Id (Ids/) — общий
Flow.Application   → Features/{Boards,Tasks}/{Commands,Queries}/*, Abstractions (IBoardRepository, ITaskItemRepository, IUnitOfWork)
Flow.Domain        → сущности Board, Status, TaskItem, TaskCode, StatusType, DefaultStatuses
Flow.Infrastructure→ EF Core (Postgres/Npgsql), репозитории, UnitOfWork, миграции
Flow.Client        → Blazor WebAssembly (заглушки, не связан с API)
```
Зависимости: Shared ← Domain ← Application ← Infrastructure ← Api (Domain зависит от Shared только ради типизированных Id).
Shared намеренно **не ссылается** на Domain (свой enum `StatusType`).

## Ключевые инварианты Board (aggregate root)
- `Key`: `^[A-Z][A-Z0-9]{1,9}$`, префикс для TaskCode.
- `Create(name, key)` — единственная публичная точка создания; сажает 4 статуса из `DefaultStatuses`.
- Максимум 1 начальный и 1 финальный статус на доску (проверяется в `AddStatus`/`SetInitialStatus`).
- `CreateTask`: без `statusId` → в начальный; `NextTaskNumber++` → TaskCode; **новые задачи регистрируются через `ITaskItemRepository.Add`**, не через коллекцию `Board.Tasks`.
- Все поля `{ get; private set; }`, мутации только через методы.

## Типизированные Id (Strongly Typed Id, формат Stripe)
- `Flow.Shared/Ids`: `ITypedId<TSelf>` (static abstract `Prefix`/`TryParse`), `TypedIdFormat` (формат/парсинг), `TypedIdJsonConverter<T>` (JSON-строка "префикс_guid").
- Id: `BoardId` ("boa"), `TaskId` ("tas"), `StatusId` ("sta") — sealed record, `New()`/`Create()`/`TryParse()`/`Parse()`, `ToString()` → `префикс_guid(N)` (32 hex без дефисов). Чужой префикс не парсится — перепутать id сущностей на входе нельзя.
- Сущности: `Board.Id`, `TaskItem.Id/BoardId/StatusId`, `Status.Id/BoardId` — типизированные; свойства инициализируются `= null!` (EF использует приватный конструктор без параметров).
- БД: id хранятся как обычный `uuid` (EF value converter в `Configurations/`) — схема и миграции от типизации не меняются.
- API: маршруты `{id}`/`{boardId}` — строки без констрейнта `:guid`; провал `TryParse` → 400. Id в JSON-теле парсит конвертер по атрибуту, `[ApiController]` сам отдаёт 400 при битом значении.
- Тесты id: `tests/Flow.Domain.Tests/TypedIdTests.cs` (Flow.Shared доступен транзитивно через Flow.Domain).

## MediatR и DI
- `AddFlowApplication()` регистрирует MediatR с reflection-сканированием сущности `Flow.Application` — хендлеры `internal`, commands/queries `public sealed record`.
- `AddFlowInfrastructure(config)` регистрирует `FlowDbContext` (UseNpgsql) + scoped-репозитории + `IUnitOfWork`.
- `IUnitOfWork.SaveChangesAsync` — единственный способ коммитить.

## API (MVC, `[ApiController]`)
| Метод | Путь |
|---|---|
| POST/GET | `/boards` |
| GET/PATCH/DELETE | `/boards/{id}`, `/boards/{id}/name` |
| POST/GET | `/boards/{boardId}/tasks` |
| GET/PATCH/DELETE | `/tasks/{id}` |

Обработка ошибок: `catch (ArgumentException or InvalidOperationException)` → 400.
`TaskUpdate` возвращает `TaskUpdateResult` (NotFound | InvalidStatus | Success+Response).
`BoardCreate` возвращает `BoardCreateResult` (KeyTaken → 409 | Success+Response); ключ проверяется через `IBoardRepository.ExistsByKeyAsync` по нормализованному `Board.Key`.
`BoardDelete` → `IBoardRepository.RemoveAsync`: репозиторий сам догружает задачи и помечает их Deleted до доски, иначе EF упрётся в FK Restrict (`TaskItems.StatusId`) — либо в БД (23503), либо на клиенте (severed required relationship).

## СБД (Postgres 16, `flow/flow/flow`, :5432)
- `Boards`: PK Id, unique Key
- `Statuses`: PK Id, FK BoardId (cascade), unique (BoardId, SortOrder) и (BoardId, Name)
- `TaskItems`: PK Id, FK BoardId (cascade), FK StatusId (restrict), unique Code
- Стр-подк: `ConnectionStrings:Postgres` (appsettings для локалки, env `ConnectionStrings__Postgres` в docker).

## Команды
- Требуется .NET SDK 10.x (закреплено в `global.json`, `rollForward: latestMinor`); SDK 8/9 сборку не соберут (NETSDK1045).
- Build/test: `dotnet build Flow.slnx`, `dotnet test Flow.slnx`
- `Flow.Infrastructure.Tests` поднимает Postgres через Testcontainers — нужен запущенный Docker.
- Migrations: `dotnet ef database update --project src/Flow.Infrastructure --startup-project src/Flow.Api`
- Docker: `docker compose up -d postgres`
- CI: GitHub Actions `.github/workflows/ci.yml` — push в любую ветку / PR в `main`: `restore` → `build -c Release` → юнит-тесты (Domain, Application) → интеграционные (Infrastructure, Testcontainers) → сборка Docker-образа `Flow.Api` без push.

## Правила стиля (унаследованы)
- Конструктор сущностей — приватный; фабрики `Board.Create`, `TaskCode.Create`.
- Методы фич живут в `Features/<Entity>/<Commands|Queries>/<Name>/<Name>.cs` + `<Name>Handler.cs`.
- Маппинг → `*MappingExtensions.ToResponse()` в `Features/<Entity>/`.
- Доменные ошибки → `ArgumentException` / `InvalidOperationException` (не `DomainException`, не `Result`).
- `TaskCode` — value object, в БД хранится как string (EF value converter).
- `Status.SortOrder`: автоинкремент при добавлении, не редактируется, только для `ORDER BY`.
- Blazor Client — заглушки (Counter/Weather), не использовать как API-клиент.

## Тесты (xUnit)
- `Flow.Domain.Tests` — юнит-тесты сущностей (Board, TaskItem, TaskCode).
- `Flow.Application.Tests` — фичи с `Fakes/` (FakeBoardRepository, FakeTaskItemRepository, FakeUnitOfWork) + `TestMediatorFactory`.
- `Flow.Infrastructure.Tests` — интеграционные на реальном Postgres (Testcontainers, образ `postgres:16-alpine`): миграции, удаление доски с задачами, дубликат ключа.

## Навигация
- Структура проекта: `docs/Struktura_board_task_status.md`
- Сравнение подходов DbContext: `docs/Sravnenie_DbContext_podhodov.md`
- ТЗ: `docs/TZ_board_task_status.md`
