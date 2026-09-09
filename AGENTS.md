# Flow — трекер задач по проектам (C#, .NET 10)

## Слойная архитектура
```
Flow.Api           → контроллеры (только IMediator + IActorAccessor); Bearer JWT от Flow.Auth (JwtBearer), fallback-политика «всё закрыто», Auth/ClaimsActorAccessor, Auth/ApiExceptionFilter, Bootstrap/BootstrapOwnerSeeder (миграции + профиль bootstrap-пользователя при старте)
Flow.Auth          → отдельный сервис аутентификации (:5100, база flow_auth): ASP.NET Core Identity + BCrypt, OpenIddict (code+PKCE, refresh, client_credentials), страница входа, admin-API /accounts. Ссылается только на Flow.Shared
Flow.Shared        → DTO-контракты (Boards, Tasks, Users, Accounts) — общий
Flow.Application   → Features/{Boards,Tasks,Users,Bootstrap}/{Commands,Queries}/*, Abstractions (IBoardRepository, ITaskItemRepository, IUserRepository, IUnitOfWork, IAccountService, IActorAccessor, IPermissionService), Security (PermissionService, ActorResolver), Exceptions (UnauthorizedActorException → 401, ForbiddenException → 403, AuthUnavailableException → 502)
Flow.Domain        → сущности Board, Status, TaskItem, TaskCode, StatusType, DefaultStatuses, User, UserLink, UserLinkType, UserRole, UserStatus
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
- Роль и статус (`docs/TZ_user_roles.md`, #15): `User.Role : UserRole` (Reader 0 … Owner 4, порядок = сила, сравнивать `>=`; по умолчанию Member) и `User.Status : UserStatus` (Invited при создании → Active после первого `GET /users/me` через `MarkActive()` → Deactivated). `IsActive`/`CanBeAssigned` вычисляются из `Status` (`builder.Ignore`), в БД их нет — в запросах фильтровать по `Status != Deactivated`. `ChangeRole` только меняет роль; «последний Owner» и права — Application (#18). `Deactivate()` бросает для Owner (сначала понизить) и повторно; `Activate()` — только из Deactivated; `MarkActive()` для Deactivated — ошибка. Пароли в домене отсутствуют намеренно (живут в Flow.Auth). Из UI деактивация убрана до #20.
- `TaskItem.CreatedById : Guid?` (FK → Users, Restrict, индекс) — кто создал, для «своей задачи» у Member; `Board.CreateTask(..., createdById)`. `TaskCreate` пишет туда actor.

## Права (docs/TZ_user_roles.md, #18)
- Все изменяющие команды Boards/Tasks/Users начинаются с `Guid ActorId` (контроллеры берут его из `IActorAccessor.Require()` — claim `sub`). Запросы actor не получают: читать могут все роли.
- Хендлер: `ActorResolver.ResolveAsync(ActorId)` (нет профиля или Deactivated → `UnauthorizedActorException` → 401) → `IPermissionService.Ensure*` (→ `ForbiddenException` → 403) → бизнес-инварианты (`InvalidOperationException` → 400). Матрица: `EnsureCanManageBoards` Admin+; `EnsureCanCreateTask` Member+; `EnsureCanEditTask`/`EnsureCanAssign` Developer+ либо Member на своей задаче (создатель или исполнитель; Member назначает только себя и снимает себя); `EnsureCanManageUsers`/`EnsureCanCreateUser` Admin+ (Admin не выдаёт Admin/Owner, роль не выше своей); `EnsureCanEditProfile` сам или Admin+; `EnsureCanEditCredentials` (username/email/пароль) сам или Owner; `EnsureCanDeactivate` Owner; `EnsureCanChangeRole` Admin+, не выше своей, себя не повышать, Admin — только цели и роли ниже Admin.
- «Последний Owner» — в `UserChangeRoleCommandHandler` через `CountByRoleAsync` (400). Owner нельзя деактивировать (домен; `UserDeactivate` проверяет до похода в Flow.Auth). `UserChangePassword`: свой — с текущим паролем (иначе 400), чужой — сброс Owner'ом без текущего.
- Тесты: `PermissionTests` — по тесту на строку матриц; `TestMediatorFactory` и `PostgresFixture` сеют Owner (`OwnerId`) — actor по умолчанию в остальных тестах.
- Учётная запись живёт в Flow.Auth (см. «Аутентификация»), профиль — здесь; связь по одному Guid. `UserCreate(…, Password)` сначала создаёт учётную запись через `IAccountService.CreateAsync(user.Id, …)`, и только при успехе — профиль; `UserChangeUsername`/`UserChangeEmail` — сначала Flow.Auth, потом копия в `Users` (нормализуют через домен, откатывают, проверяют, применяют — иначе Fake-репозиторий «находит» самого пользователя); `UserDeactivate`/`UserActivate` — `DisableAsync`/`EnableAsync` до смены статуса; `UserChangePassword(UserId, CurrentPassword?, NewPassword)` — только Flow.Auth. Отказ Flow.Auth по вводу → `ArgumentException` → 400, недоступность → `AuthUnavailableException` (502 — #24).
- Уникальность username/email: локальная копия — быстрый 409 без похода в Flow.Auth (`UserCreateResult.IsUsernameTaken`/`IsEmailTaken`, `UserUpdateResult.ConflictError`); источник истины — Identity в Flow.Auth (`AccountResult.UsernameTaken/EmailTaken`); плюс unique-индексы в БД.
- `Features/Bootstrap/SeedBootstrapUserCommand` — профиль базового пользователя с заданным Id (`User.CreateWithId`), Flow.Auth не вызывает (учётную запись с тем же Id сеет он сам); повтор — no-op. Запускается hosted service Flow.Api (#24). `UserGetMeQuery(ActorId)` — профиль текущего actor (null → 401).
- Все изменяющие команды пользователя возвращают общий `Features/Users/UserUpdateResult` (NotFound | ConflictError | Success). `UserUpdateProfile` — PATCH-семантика: null не трогать, пустая строка очищает.
- Назначение на задачу: `TaskItem.AssigneeId : Guid?`, `Assign`/`Unassign`; **назначать можно только активного пользователя** — проверяет `TaskAssignCommandHandler` (`TaskAssignResult`: NotFound | ValidationError | Success). `TaskListQuery(boardId, assigneeId?)` фильтрует по исполнителю.
- `IUserRepository.SearchAsync` — автодополнение для `@` (ILIKE по Username/FirstName/LastName, только активные, `%`/`_` экранируются).
- План по слоям: GitHub #8 (подзадачи #9–#13), ТЗ — `docs/TZ_user.md` (там же чек-лист перевода на `UserId` после мержа ветки `StronglyTypeId`).

## Аутентификация (Flow.Auth) — docs/TZ_auth.md, GitHub #21
- Учётные записи (username, email, пароль, блокировка) — в `Flow.Auth` (`AspNetUsers`, `ApplicationUser : IdentityUser<Guid>`); профиль, роль и статус — в `Flow.Api` (`Users`). Связь по одному Guid: `AspNetUsers.Id = Users.Id`.
- Пароли — BCrypt (`Security/BCryptPasswordHasher`, `EnhancedHashPassword`, work factor `Auth:BCrypt:WorkFactor` = 12) вместо PBKDF2 Identity. Политика: ≥ 8 символов, без других требований; lockout 5 неудач → 5 минут; деактивация = `LockoutEnd = MaxValue` (`POST /accounts/{id}/disable`).
- OpenIddict: `/connect/authorize|token|endsession|userinfo`, клиент `flow-client` (public, PKCE) и `flow-api` (confidential, `Auth:ApiClient:Secret`, scope `auth:admin`) сеются `Security/ClientSeeder` из конфига при старте. Access token — незашифрованный JWT, `aud = flow-api`; роли/статуса в токене нет — Flow.Api читает их из своей БД.
- Admin-API `/accounts` (`[Authorize(Policy = AuthAdmin)]`: Bearer с `aud = flow-auth` и scope `auth:admin`): `POST /accounts` (Id задаёт вызывающий), `PATCH /accounts/{id}/username|email`, `POST /accounts/{id}/password|disable|enable`, `GET /accounts/{id}`. Контракты — `Flow.Shared/Contracts/Accounts`.
- Bootstrap: `Security/BootstrapUserSeeder` создаёт пользователя из секции `Bootstrap` (`admin@flow.com` / `admin`, Id `11111111-…`), хеш пишет напрямую (валидаторы пароля не применяются), идемпотентно. Flow.Api сеет профиль Owner с тем же Id (#24).
- Старт: `Security/AuthDatabaseInitializer` — миграции → клиенты → bootstrap. Ключи: Development — dev-сертификаты OpenIddict; `Auth:UseEphemeralKeys` — тесты; иначе PFX из `Auth:SigningCertificate`/`Auth:EncryptionCertificate` (Docker: `sh docker/auth/make-certs.sh`). `Auth:AllowInsecureHttp` — http вне Development (локальный compose). `DataProtection:KeysPath` — persist ключей cookie/antiforgery в контейнере.
- Страница входа `Pages/Account/Login` (логин = username или email); Razor настроен на `UnicodeRanges.All`, иначе кириллица уходит как `&#x...;`.
- Миграции: `cd src/Flow.Auth && dotnet ef migrations add <Name> --output-dir Migrations` (design-time фабрика `Data/AuthDbContextFactory`, хост не поднимается).
- Flow.Api как resource server: `AddJwtBearer(Authority = Auth:BaseUrl, Audience = flow-api, ValidIssuer = Auth:Issuer ?? BaseUrl, MapInboundClaims = false)`; `FallbackPolicy = RequireAuthenticatedUser` — анонимен только `GET /`. Actor — `IActorAccessor` (`Auth/ClaimsActorAccessor`, claim `sub`). `AuthUnavailableException` → 502 `{ message }` в `Auth/ApiExceptionFilter` (глобальный фильтр MVC). Заголовка `X-Actor-Id` нет и не будет.
- Flow.Api при старте (`Bootstrap/BootstrapOwnerSeeder`): `FlowDbContext.Database.MigrateAsync()` → `SeedBootstrapUserCommand` из секции `Bootstrap` (`Enabled=false` отключает). В Docker миграции больше руками не нужны.
- Ручные запросы к Flow.Api: токен берётся из клиента (sessionStorage после входа) или через code+PKCE у Flow.Auth; токен `flow-api` (client_credentials) к Flow.Api **не подходит** — у него `aud = flow-auth`.
- Flow.Api → Flow.Auth: `Flow.Infrastructure/Auth/AuthAccountService : IAccountService` (typed HttpClient) + `ClientCredentialsTokenProvider` (singleton, кэш токена flow-api до exp−30с, повтор один раз после 401). Секция `Auth` в Flow.Api: `BaseUrl`, `ApiClient:ClientId/Secret`. Без настроек — `AuthUnavailableException` при первом вызове, не на старте.

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
| POST/GET | `/users` (POST требует `Password` → учётная запись в Flow.Auth), `/users/search?q=&limit=` |
| GET | `/users/me` — профиль текущего actor (claim sub); 401, если профиля нет |
| POST | `/users/{id}/password` — `{ currentPassword?, newPassword }`; null = сброс (право Owner — #18) |
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
- `TaskItems`: PK Id, FK BoardId (cascade), FK StatusId (restrict), FK AssigneeId и FK CreatedById → Users (restrict, nullable, индексы), unique Code
- `Users`: PK Id, unique Username, unique Email (значения уже lower — индексы регистронезависимы по факту), `Role int`, `Status int`, `StatusChangedAt timestamptz null`. Миграция `AddUserRoleAndStatus` переносит данные вручную (Up переписан: сначала новые колонки и UPDATE, потом DropColumn IsActive / Rename DeactivatedAt): Status из IsActive, самый ранний по CreatedAt — Owner.
- `UserLinks`: owned-коллекция `User.Links`, PK (UserId, Type), FK UserId (cascade)
- Стр-подк: `ConnectionStrings:Postgres` (appsettings для локалки, env `ConnectionStrings__Postgres` в docker).

## Команды
- Требуется .NET SDK 10.x (закреплено в `global.json`, `rollForward: latestMinor`); SDK 8/9 сборку не соберут (NETSDK1045).
- Build/test: `dotnet build Flow.slnx`, `dotnet test Flow.slnx`
- `Flow.Infrastructure.Tests`, `Flow.Auth.Tests`, `Flow.Api.Tests` поднимают Postgres через Testcontainers — нужен запущенный Docker.
- Migrations: `dotnet ef database update --project src/Flow.Infrastructure --startup-project src/Flow.Api`
- Docker, весь стек одной командой: `cp .env.example .env` (на машине с локальным Postgres — `POSTGRES_PORT=5433`), затем `docker compose up -d --build` → клиент http://localhost:5016, Api :8080, Auth :5100, вход `admin@flow.com` / `admin`. Сертификаты Flow.Auth создаются сами в volume `auth-certs`, базы `flow`/`flow_auth` — миграциями при старте. Dockerfile лежат в проектах (`src/Flow.Api|Flow.Auth|Flow.Client/Dockerfile`, Rider-конвенция; у библиотек и тестов их нет). Клиент — nginx ( `docker/client/entrypoint.sh` пишет `appsettings.json` из `API_BASE_URL`/`AUTH_BASE_URL`). Api внутри compose берёт discovery/JWKS по `http://auth:8080`, а issuer проверяет внешний (`Auth/IssuerRewritingConfigurationRetriever`), `Auth__RequireHttpsMetadata=false`. Только Postgres: `docker compose up -d postgres`. В Rider — общие run-конфигурации `.run/Flow (docker compose)` (up --build) и `.run/Flow (postgres only)`. Базовый пользователь — якорь `x-bootstrap` в compose (`BOOTSTRAP_*`).
- Клиент локально: `dotnet run --project src/Flow.Api` (:5000) + `dotnet run --project src/Flow.Client` (:5016); клиент читает `ApiBaseUrl` из `wwwroot/appsettings.json`.
- CI: GitHub Actions — `ci.yml` (триггеры и порядок) вызывает `build.yml` (restore → build Release → юнит-тесты → интеграционные на Testcontainers) и `docker.yml` трижды (образы `Flow.Api`, `Flow.Auth`, `Flow.Client` по `src/<проект>/Dockerfile`; с `main` публикуются в registry по секретам `REGISTRY_USERNAME`/`REGISTRY_PASSWORD` и переменным `REGISTRY`/`IMAGE_NAME`/`AUTH_IMAGE_NAME`/`CLIENT_IMAGE_NAME`).

## Правила стиля (унаследованы)
- Конструктор сущностей — приватный; фабрики `Board.Create`, `TaskCode.Create`.
- Методы фич живут в `Features/<Entity>/<Commands|Queries>/<Name>/<Name>.cs` + `<Name>Handler.cs`.
- Маппинг → `*MappingExtensions.ToResponse()` в `Features/<Entity>/`.
- Доменные ошибки → `ArgumentException` / `InvalidOperationException` (не `DomainException`, не `Result`).
- `TaskCode` — value object, в БД хранится как string (EF value converter).
- `Status.SortOrder`: автоинкремент при добавлении, не редактируется, только для `ORDER BY`.
- Blazor Client: Board в UI называется «проект», задачи — плоский список со статусом и исполнителем в строке (не канбан). DTO только из Flow.Shared, ничего не дублировать. Стили — DRESSY-токены (`wwwroot/css/tokens.css`) + классы из макета (`app.css`); иконки — DRESSY-глифы в `Components/DressyIcons.cs` (плюс контурные `user`, `user-plus`, `user-off`, `at`, `mail`, `phone`, `link`, `briefcase`, `check-circle`, `external` для раздела «Люди»). Даты/склонения/username-транслит — `Services/Ru.cs` (InvariantGlobalization включён). Адрес API — `wwwroot/appsettings.json` → `ApiBaseUrl`; в `Flow.Api` CORS-origins клиента — `Cors:Origins`.
- Blazor Client, вход: `AddOidcAuthentication` (authority `AuthBaseUrl` из `wwwroot/appsettings.json`, client `flow-client`, code + PKCE, scopes `openid profile email offline_access flow-api`), `_content/…/AuthenticationService.js` в `index.html`. Все страницы `[Authorize]` через `_Imports.razor`; `App.razor` — `AuthorizeRouteView`, без сессии `RedirectToLogin` → `Pages/Authentication.razor` (`/authentication/{action}`, `RemoteAuthenticatorView`, `AuthLayout` без сайдбара). `HttpClient` к Flow.Api — через `IHttpClientFactory` с `Services/FlowAuthorizationMessageHandler` (Bearer только на `ApiBaseUrl`); нет токена → `AccessTokenNotAvailableException` → редирект на вход. `ApiResult`: `Unauthorized`/`Forbidden`, тексты для 401/403/502. «Выйти» — иконка в футере сайдбара → `NavigateToLogout("authentication/logout", "authentication/logged-out")` (без явного returnUrl библиотека вернёт на закрытую страницу и тут же начнёт новый вход). Ловушка: `AuthorizeRouteView` рисует `Authorizing`/`NotAuthorized` внутри `DefaultLayout` (MainLayout) — поэтому `Sidebar` спрятан за `AuthorizeView` и сам не ходит в API без сессии, иначе `AccessTokenNotAvailableException` уводил бы на вход даже с анонимных страниц. Пароль: карточка «Пароль» в профиле (`POST /users/{id}/password`: свой — с текущим, чужой — сброс с генератором `Services/Passwords`), поле «Начальный пароль» в модалке нового человека.
- Blazor Client, пользователи: справочник `Services/UserDirectory` (один `GET /users?includeInactive=true` на сессию, `Find(id)`, `Put(user)` после изменений, событие `Changed`) — строки задач берут аватар исполнителя из него, а не через `GET /users/{id}`. Аватар — `Components/Avatar` (картинка или инициалы на тинте по Id, `Services/Avatars`). Выбор исполнителя — `AssigneeSelect` (поповер с локальным фильтром по активным), фильтр списка — `AssigneeFilter` (`?who=<guid>|none`), карточка по клику — `UserCard`. Текущий пользователь — claim `sub` токена → `AppState.CurrentUserId` (ставит `Sidebar` после `GET /users/me`); ручного «Это я» и localStorage `flow.me` больше нет. Макеты: артефакт Claude Design «Flow CRM» (Profile.dc.html → страница профиля), перенесены в тёмную тему Flow.
- Motion (по emilkowalski/skills, `emil-design-eng`): анимируем только transform/opacity, кривые из `tokens.css` (`--ease-out`, `--ease-drawer`), UI ≤ 300ms, press-feedback `scale(.97)`, поповеры от якоря (`transform-origin`), hover только под `(hover: hover) and (pointer: fine)`, `prefers-reduced-motion` снимает transform-движение. Хоткеи (N, Esc, ↑/↓) — без анимаций.

## Тесты (xUnit)
- `Flow.Domain.Tests` — юнит-тесты сущностей (Board, TaskItem, TaskCode, User — включая роли/статусы).
- `Flow.Application.Tests` — фичи с `Fakes/` (FakeBoardRepository, FakeTaskItemRepository, FakeUserRepository, FakeAccountService, FakeUnitOfWork) + `TestMediatorFactory` (`Create()` — 4 фейка, `CreateWithAccounts()` — плюс FakeAccountService для ассертов на вызовы в Flow.Auth). `AccountFeatureTests` — связка Users ↔ Flow.Auth и bootstrap.
- `Flow.Infrastructure.Tests` — интеграционные на реальном Postgres (Testcontainers, образ `postgres:16-alpine`): миграции, удаление доски с задачами, дубликат ключа, пользователи (ссылки, unique-индексы, поиск). `PostgresFixture` подменяет `IAccountService` на `AlwaysSucceedingAccountService` — в Flow.Auth не ходит. `AuthAccountServiceTests` — юнит-тесты HTTP-клиента admin-API на `HttpMessageHandler`-заглушке (маппинг 409/400/5xx, кэш токена, повтор после 401). `RoleMigrationTests` — свой контейнер: миграция до `AddTaskAssignee`, SQL-вставка со старыми колонками, миграция до конца, проверка переноса IsActive → Status и Owner у самого раннего.
- `Flow.Api.Tests` — `WebApplicationFactory<Program>` + Testcontainers (`ApiFixture`): JwtBearer переключён на локальный симметричный ключ (`PostConfigure<JwtBearerOptions>`: Authority/ConfigurationManager = null), токены выпускает `ApiFixture.CreateToken(sub)`, `IAccountService` → `FakeAccountService` из Flow.Application.Tests. Кейсы: 401 без токена / чужой aud / чужой iss, `/users/me`, обязательный пароль при `POST /users`, 502 при недоступном Flow.Auth, bootstrap-профиль после старта и идемпотентность сидера.
- `Flow.Auth.Tests` — `WebApplicationFactory<Program>` + Testcontainers (`AuthFixture`, один хост на коллекцию, эфемерные ключи): discovery/JWKS, admin-API (401/409/400), BCrypt-хеш `$2a$12$`, lockout, disable, bootstrap-пользователь, полный code+PKCE → JWT → refresh. Страница входа проходится как браузером (`LoginPage`: antiforgery из HTML).

## Навигация
- Структура проекта: `docs/Struktura_board_task_status.md`
- Сравнение подходов DbContext: `docs/Sravnenie_DbContext_podhodov.md`
- ТЗ: `docs/TZ_board_task_status.md`, `docs/TZ_user.md`, `docs/TZ_user_roles.md` (роли/статусы — план, #15), `docs/TZ_auth.md` (аутентификация, #21)
