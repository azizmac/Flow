# Структура решения: Board / TaskItem / Status

## Слои

```
Flow.Domain          — сущности (persistence-ignorant)
    ↑
Flow.Application     — бизнес-логика: Features/{Boards,Tasks}/{Commands,Queries}/*,
    ↑                   абстракции репозиториев/UnitOfWork, ISender-диспетчер
Flow.Infrastructure  — EF Core DbContext, конфигурации, реализации репозиториев из
    ↑                   Flow.Application.Abstractions, миграции
Flow.Api             — контроллеры (только вызывают ISender), appsettings.json (ConnectionStrings)

Flow.Shared          — DTO-контракты API (используются Flow.Application, Flow.Api и Flow.Client)
```

### Features (`src/Flow.Application/Features`)

Вертикальные срезы по сущности, внутри — `Commands/` и `Queries/`, у каждой команды/запроса своя папка с двумя файлами:

```
Features/Boards/
├── Commands/
│   ├── BoardCreateCommand/{BoardCreateCommand.cs, BoardCreateCommandHandler.cs}
│   └── BoardRenameCommand/{BoardRenameCommand.cs, BoardRenameCommandHandler.cs}
└── Queries/
    ├── BoardGetQuery/{BoardGetQuery.cs, BoardGetQueryHandler.cs}
    └── BoardListQuery/{BoardListQuery.cs, BoardListQueryHandler.cs}

Features/Tasks/
├── Commands/
│   ├── TaskCreateCommand/{TaskCreateCommand.cs, TaskCreateCommandHandler.cs}
│   └── TaskUpdateCommand/{TaskUpdateCommand.cs, TaskUpdateCommandHandler.cs, TaskUpdateResult.cs}
└── Queries/
    ├── TaskGetQuery/{TaskGetQuery.cs, TaskGetQueryHandler.cs}
    └── TaskListQuery/{TaskListQuery.cs, TaskListQueryHandler.cs}
```

- Команда/запрос (`BoardCreateCommand` и т.п.) — `public sealed record`, реализует `IRequest<TResponse>`. Это единственное, что видит `Flow.Api`.
- Хендлер (`BoardCreateCommandHandler` и т.п.) — `internal sealed class`, реализует `IRequestHandler<TRequest, TResponse>`. Не виден за пределами сборки `Flow.Application` — контроллеры не могут (и не должны) обращаться к нему напрямую.
- `ISender` (`Flow.Application/Abstractions/ISender.cs`, реализация `Flow.Application/Dispatching/Sender.cs`) — рефлексией находит зарегистрированный в DI `IRequestHandler<,>` под тип конкретной команды и вызывает его. Это самодельный мини-медиатор (без MediatR — у него с недавних версий платная лицензия для не самых маленьких команд, а для двух сущностей это лишняя зависимость).
- Регистрация всех хендлеров — `Flow.Application/DependencyInjection/FlowApplicationServiceCollectionExtensions.AddFlowApplication()`, вызывается из `Flow.Api/Program.cs`.

Контроллер выглядит так:
```csharp
var response = await sender.SendAsync(new BoardCreateCommand(request.Name, request.Key), cancellationToken);
```
— никакого `FlowDbContext`, никакой доменной логики, только маппинг HTTP ⇄ команда/результат.

## Сущности (`src/Flow.Domain/Entities`)

### `Board` — aggregate root
| Поле | Тип | Описание |
|---|---|---|
| `Id` | `Guid` | Первичный ключ |
| `Key` | `string` | Короткий код доски в верхнем регистре (`^[A-Z][A-Z0-9]{1,9}$`), префикс кода задачи |
| `Name` | `string` | Название доски |
| `CreatedAt` | `DateTime` | Дата создания |
| `NextTaskNumber` | `int` | Счётчик для генерации следующего номера в `TaskCode` |
| `Statuses` | `IReadOnlyCollection<Status>` | Статусы задач, настроенные на этой доске |
| `Tasks` | `IReadOnlyCollection<TaskItem>` | Задачи доски |

Методы: `Create(name, key)` (засеивает статусы из `DefaultStatuses`), `Rename(name)`, `AddStatus(name, type?, isInitial, isFinal)`, `SetInitialStatus(statusId)`, `CreateTask(title, description, statusId?)`.

Конструктор `Board` — приватный: единственная публичная точка создания — `Board.Create(...)`. Если бы конструктор был публичным, `new Board(name, key)` создавал бы доску без единого статуса, и `CreateTask()` падал бы с «нет начального статуса» — недостижимое сейчас состояние было бы легко получить по ошибке.

### `Status` — сущность, настраиваемая на уровне доски
| Поле | Тип | Описание |
|---|---|---|
| `Id` | `Guid` | Первичный ключ |
| `BoardId` | `Guid` | FK на `Board` |
| `Name` | `string` | Название статуса |
| `SortOrder` | `int` | Порядковый номер колонки на доске |
| `IsInitial` | `bool` | Статус по умолчанию для новых задач (максимум один на доску) |
| `IsFinal` | `bool` | Финальный статус (максимум один на доску) |
| `Type` | `StatusType?` | Из какого пресета создан статус; `null` — кастомный, добавленный вручную через `AddStatus` |

Создаётся только через `Board.AddStatus(...)`. Все поля — `{ get; private set; }`, менять можно только через методы (`Rename`) — публичных сеттеров нет намеренно, чтобы нельзя было обойти валидацию (пустое имя) или инвариант «один начальный/один финальный статус на доску», который проверяется в `Board.AddStatus`/`SetInitialStatus`, а не в самой `Status`. `SortOrder` проставляется автоматически при добавлении статуса (`Board.AddStatus`) и не редактируется — нужен только для стабильной сортировки при выводе списка статусов (Postgres не гарантирует порядок строк без `ORDER BY`); переставлять статусы местами (drag-n-drop колонок) сейчас не требуется — задача просто переключается между уже существующими статусами через `TaskItem.ChangeStatus`.

### `StatusType` (enum) и `DefaultStatuses`
`StatusType` — `NotStarted | InProgress | InReview | Done`. `DefaultStatuses.All` — готовый список из 4 записей (`Type`, `Name`, `IsInitial`, `IsFinal`), которым `Board.Create` засеивает новую доску (просто перебирает список и вызывает `AddStatus` для каждой записи — никаких магических строк в теле `Create`). Статусы, добавленные вручную через `Board.AddStatus(name)` без явного `type`, получают `Type = null`.

### `TaskItem` — сущность
| Поле | Тип | Описание |
|---|---|---|
| `Id` | `Guid` | Первичный ключ |
| `BoardId` | `Guid` | FK на `Board` |
| `Code` | `TaskCode` | Человекочитаемый код (`FLW-42`), хранится как `string` через value converter |
| `Title` | `string` | Название задачи |
| `Description` | `string?` | Описание задачи |
| `StatusId` | `Guid` | FK на `Status` |
| `AssigneeId` | `Guid?` | FK на `User`, исполнитель; null — не назначен |
| `CreatedAt` | `DateTime` | Дата создания задачи |

Создаётся только через `Board.CreateTask(...)`. Методы: `Rename(title)`, `UpdateDescription(description)`, `ChangeStatus(statusId)`, `Assign(userId)`, `Unassign()`.

### `TaskCode` — value object
Оборачивает строку `{Key доски}-{NextTaskNumber}`. Создаётся через `TaskCode.Create(boardKey, number)` (генерация) или `TaskCode.FromValue(value)` (восстановление из БД, используется в EF Core value converter).

## Схема БД (PostgreSQL, стандартная EF Core-нотация — PascalCase, без `snake_case`)

| Таблица | Ключи/индексы |
|---|---|
| `Boards` | PK `Id`; уникальный индекс на `Key` |
| `Statuses` | PK `Id`; FK `BoardId` → `Boards` (cascade delete); уникальные индексы на `(BoardId, SortOrder)` и `(BoardId, Name)` |
| `TaskItems` | PK `Id`; FK `BoardId` → `Boards` (cascade delete); FK `StatusId` → `Statuses` (restrict delete); FK `AssigneeId` → `Users` (restrict delete, nullable, индекс); уникальный индекс на `Code` |
| `Users` | PK `Id`; уникальные индексы на `Username` и `Email` |
| `UserLinks` | owned-коллекция `User.Links`; PK `(UserId, Type)`; FK `UserId` → `Users` (cascade delete) |

## Конфигурация PostgreSQL

- Docker: `docker-compose.yml` уже поднимает `postgres:16-alpine` (база/юзер/пароль `flow`/`flow`/`flow`, порт `5432` на хосте).
- `src/Flow.Api/appsettings.json` → `ConnectionStrings:Postgres` (для локального запуска вне контейнера, `Host=localhost`).
- В контейнере значение приходит из `ConnectionStrings__Postgres` (env var в `docker-compose.yml`), appsettings не используется.
- Регистрация: `Flow.Infrastructure/DependencyInjection/FlowInfrastructureServiceCollectionExtensions.AddFlowInfrastructure(configuration)`, вызывается из `Flow.Api/Program.cs`.

### Применение первой миграции

```bash
docker compose up -d postgres
dotnet ef migrations add InitialCreate --project src/Flow.Infrastructure --startup-project src/Flow.Api
dotnet ef database update --project src/Flow.Infrastructure --startup-project src/Flow.Api
```

## API-эндпоинты (`src/Flow.Api/Controllers`)

Классические MVC-контроллеры (`[ApiController]`, `ControllerBase`, DI через конструктор) — `BoardsController` (`[Route("boards")]`) и `TasksController` (без общего префикса, у каждого action свой полный путь, т.к. задачи живут и под `boards/{boardId}/tasks`, и под `tasks/{id}`).


| Метод | Путь | Описание |
|---|---|---|
| `POST` | `/boards` | Создать доску |
| `GET` | `/boards` | Список досок |
| `GET` | `/boards/{id}` | Доска по id (со статусами) |
| `PATCH` | `/boards/{id}/name` | Переименовать доску |
| `POST` | `/boards/{boardId}/tasks` | Создать задачу на доске |
| `GET` | `/boards/{boardId}/tasks?assigneeId=` | Список задач доски, опционально по исполнителю |
| `GET` | `/tasks/{id}` | Задача по id |
| `PATCH` | `/tasks/{id}` | Обновить задачу (название/описание/статус — только переданные поля) |
| `PATCH` | `/tasks/{id}/assignee` | Назначить исполнителя (`userId: null` — снять; неактивный/неизвестный → 400) |
| `POST` | `/users` | Создать пользователя (409 — username/email занят) |
| `GET` | `/users?includeInactive=false` | Список пользователей |
| `GET` | `/users/search?q=&limit=10` | Автодополнение для `@` (только активные) |
| `GET` | `/users/{id}`, `/users/by-username/{username}` | Пользователь по id / username |
| `PATCH` | `/users/{id}` | Профиль (только переданные поля; пустая строка очищает) |
| `PATCH` | `/users/{id}/username`, `/users/{id}/email` | Сменить username / email (409 — занят) |
| `PUT` / `DELETE` | `/users/{id}/links/{type}` | Добавить-или-заменить / удалить внешнюю ссылку |
| `POST` | `/users/{id}/deactivate`, `/users/{id}/activate` | Деактивация вместо удаления (400 — повторно) |

DTO-контракты — в `src/Flow.Shared/Contracts/Boards`, `src/Flow.Shared/Contracts/Tasks` и `src/Flow.Shared/Contracts/Users`.

Сущность `User` (поля, методы, инварианты) описана в [`TZ_user.md`](TZ_user.md).
