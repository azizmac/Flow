# ТЗ: сервис аутентификации Flow.Auth (OpenIddict + ASP.NET Core Identity + BCrypt)

## Исходное требование

Аутентификацию вынести в **отдельный сервис** `Flow.Auth`. Учётные записи хранить через **ASP.NET Core Identity**,
пароли хешировать **BCrypt**. Токены выдаёт **OpenIddict**. Flow.Api и Flow.Client привязываются к этой форме:
API проверяет Bearer-токен, «кто действует» (actor) берётся из claim `sub`, заголовок `X-Actor-Id` из ТЗ ролей не делается.

Область ТЗ: новый проект `Flow.Auth` (+ тесты), правки `Flow.Application`/`Flow.Infrastructure`/`Flow.Api`
(защита, actor, синхронизация учётных записей), `Flow.Client` (OIDC-вход), инфраструктура (docker-compose, CI, slnx).

## Границы: кто чем владеет

| Данные | Владелец | Кто ещё хранит |
|---|---|---|
| Username, email, пароль, блокировка входа | **Flow.Auth** (Identity, таблица `AspNetUsers`) | Flow.Api держит копию username/email в `Users` для списков и поиска |
| Профиль: имя, фамилия, должность, ссылки, аватар | **Flow.Api** (`Users`) | — |
| Роль workspace (Reader…Owner) и статус (Invited/Active/Deactivated) | **Flow.Api** (`Users`, ТЗ #15) | Auth знает только «вход разрешён/заблокирован» |
| Клиенты OAuth, коды, refresh-токены | **Flow.Auth** (OpenIddict) | — |

Ключ связи — один и тот же `Guid`: `AspNetUsers.Id` = `Users.Id`. Auth создаёт учётную запись, Flow.Api создаёт
профиль с тем же Id.

## Принятые решения

- **Auth — отдельный процесс и отдельная база `flow_auth`** в том же Postgres. Свои миграции, свой `AuthDbContext`
  (Identity + OpenIddict). Flow.Api в базу Auth не ходит и наоборот; общение только по HTTP.
- **Поток для клиента — Authorization Code + PKCE + refresh token.** Страница входа — Razor Pages в `Flow.Auth`.
- **Identity как хранилище**, но без UI-шаблонов Identity: `AddIdentityCore<ApplicationUser>()` + `SignInManager`,
  свои страницы. `ApplicationUser : IdentityUser<Guid>`, `IdentityRole<Guid>` регистрируется, но роли Identity
  **не используются**: роль workspace живёт в Flow.Api (нужны подсчёт «последний Owner» и мгновенное применение).
  Если позже роль понадобится в токене — Auth будет спрашивать её у Flow.Api при выдаче, а не хранить.
- **BCrypt вместо PBKDF2 Identity**: `BCrypt.Net-Next`, work factor 12, регистрируется как
  `IPasswordHasher<ApplicationUser>`. `VerifyHashedPassword` возвращает `SuccessRehashNeeded`, если work factor в хеше
  ниже текущего — Identity сама перехеширует при входе.
- **Access token — подписанный JWT без шифрования** (`DisableAccessTokenEncryption()`), audience `flow-api`.
  Flow.Api проверяет его штатным `AddJwtBearer` по discovery/JWKS Auth и **не зависит от OpenIddict**.
  Время жизни: access 60 мин, refresh 14 дней, одноразовые (rolling).
- **Роль и статус в токен не кладём.** Flow.Api загружает actor из своей `Users` на каждую команду (нужно и для #15).
- **Служебные вызовы Flow.Api → Flow.Auth** (создать учётную запись, сменить username/email, сбросить пароль,
  заблокировать/разблокировать) идут через admin-API Auth, защищённый **client credentials**: клиент `flow-api`
  (confidential, секрет из конфига) и scope `auth:admin`. В Application это абстракция `IAccountService`.
- **Auth — источник истины для username/email.** Все изменения идут через Flow.Api → Auth → при успехе Flow.Api
  обновляет свою копию. Уникальность проверяет Identity (409 транслируется как сейчас), локальные unique-индексы
  остаются страховкой.
- **Деактивация** (Owner, #15): Flow.Api сначала вызывает Auth `disable` (Identity lockout навсегда — refresh и вход
  отказываются), потом ставит `Status = Deactivated`. Ошибка Auth → 502, ничего не меняется. Живой access token
  дорабатывает до 60 минут — осознанный компромисс базовой версии.
- **Базовый пользователь создаётся при первом запуске из конфигурации** (секция `Bootstrap`, в Docker — env
  `Bootstrap__Email=admin@flow.com`, `Bootstrap__Password=admin` и т.д.). Каждый сервис сеет **свою половину** с одним
  и тем же заранее заданным `Bootstrap:Id`: Flow.Auth — учётную запись Identity, Flow.Api — профиль Owner/Active.
  Между сервисами при старте вызовов нет, порядок запуска не важен, повторный запуск ничего не делает (идемпотентно).
  Экрана «Создать workspace» и анонимных `/setup`-эндпоинтов нет. Пароль из env может быть любым (в том числе `admin`
  короче политики в 8 символов): сидер пишет хеш напрямую, минуя валидаторы Identity, это решение оператора.
  После первого входа пароль меняется в профиле; принудительная смена — вне ТЗ.
- **`Invited → Active`** — при первом `GET /users/me` от этого `sub` (клиент вызывает его при старте). Auth о входах Flow не сообщает.
- **Защита от перебора** — lockout Identity: 5 неудач → 5 минут. Отдельный rate limiter не нужен.
- **Начальный пароль обязателен к смене** (#26): пароль, заданный не самим человеком (bootstrap, админ при создании,
  сброс Owner'ом), помечается `MustChangePassword`; пока флаг стоит, Flow.Auth после входа показывает страницу смены и не
  выдаёт код клиенту. Снимается сменой пароля с текущим. Реализовано внутри Flow.Auth, клиент и Api не участвуют.
- **Ключи**: Development — `AddDevelopmentEncryptionCertificate()/SigningCertificate()`; Docker/Production — два PFX
  из `Auth:SigningCertificate:*` (подпись access/id token) и `Auth:EncryptionCertificate:*` (коды и refresh-токены
  OpenIddict шифрует всегда, даже при незашифрованном access token). Без сертификатов в Production сервис не стартует.

## Что видит пользователь

1. Открывает Flow → нет сессии → редирект на `{Auth}/connect/authorize` → страница `{Auth}/account/login`.
2. Вводит `@username` или email и пароль. Ошибка — «Неверный логин или пароль». Заблокирован —
   «Доступ закрыт. Обратитесь к основателю workspace». После 5 неудач — «Слишком много попыток, подождите 5 минут».
3. Возвращается в клиент с токенами. В футере сайдбара — он сам (`GET /users/me`), пункт «Выйти».
4. «Выйти» → `{Auth}/connect/endsession` → cookie Auth гасится, клиент возвращается на страницу входа.
5. Первый запуск: в базе уже есть `admin@flow.com` / `admin` (или то, что задано в env) с ролью Owner. Входит им,
   создаёт остальных людей, меняет себе пароль в профиле.

## Flow.Auth (новый проект `src/Flow.Auth`)

### Пакеты

`OpenIddict.AspNetCore 7.6.0`, `OpenIddict.EntityFrameworkCore 7.6.0`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.0`,
`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0`, `BCrypt.Net-Next 4.0.3`.

### Структура

```
src/Flow.Auth/
  Program.cs
  Data/AuthDbContext.cs            IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid> + UseOpenIddict()
  Data/ApplicationUser.cs          : IdentityUser<Guid>
  Migrations/                      InitialAuth (AspNet* + OpenIddict*)
  Security/BCryptPasswordHasher.cs IPasswordHasher<ApplicationUser>
  Security/ClientSeeder.cs         IHostedService: клиенты flow-client, flow-api и scope'ы
  Security/BootstrapUserSeeder.cs  IHostedService: учётная запись базового пользователя из Bootstrap:*
  Controllers/AuthorizationController.cs   /connect/authorize, /connect/token, /connect/endsession (passthrough)
  Controllers/AccountsController.cs        admin-API, [Authorize(Policy = "auth:admin")]
  Pages/Account/Login.cshtml(.cs), Logout.cshtml(.cs)
  wwwroot/css/auth.css             копия DRESSY-токенов
  appsettings.json
```

### Identity

- `AddIdentityCore<ApplicationUser>(o => { Password: RequiredLength 8, без RequireDigit/Upper/NonAlphanumeric; User.RequireUniqueEmail = true;
  User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789._-"; Lockout: MaxFailedAccessAttempts 5, DefaultLockoutTimeSpan 5 мин, AllowedForNewUsers true })`
  `.AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AuthDbContext>().AddSignInManager()`.
- `services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher>()` **после** `AddIdentityCore`, чтобы перекрыть дефолтный.
- Формат username (`^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$`) валидирует Flow.Api до вызова Auth; Auth дополнительно ограничивает алфавит.
- Cookie-схема `Identity.Application` (даёт `AddSignInManager`), `LoginPath = /account/login`, 8 часов, sliding.

### `BCryptPasswordHasher`

```csharp
public sealed class BCryptPasswordHasher(IOptions<BCryptOptions> options) : IPasswordHasher<ApplicationUser>
{
    public string HashPassword(ApplicationUser user, string password)
        => BCrypt.Net.BCrypt.EnhancedHashPassword(password, options.Value.WorkFactor); // 12

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (!BCrypt.Net.BCrypt.EnhancedVerify(providedPassword, hashedPassword))
            return PasswordVerificationResult.Failed;

        return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, options.Value.WorkFactor)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }
}
```

`Enhanced*` — SHA-384 перед BCrypt, снимает лимит 72 байта на пароль. Work factor — `Auth:BCrypt:WorkFactor` (12).

### Сервер OpenIddict

- Эндпоинты: `/connect/authorize`, `/connect/token`, `/connect/endsession`, `/connect/userinfo`, `/.well-known/openid-configuration`.
- `AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()`, `AllowRefreshTokenFlow()`, `AllowClientCredentialsFlow()` (для flow-api).
- Скоупы: `openid profile email offline_access flow-api auth:admin`. Ресурсы: scope `flow-api` → audience `flow-api`, `auth:admin` → audience `flow-auth`.
- `DisableAccessTokenEncryption()`, `SetAccessTokenLifetime(60 мин)`, `SetRefreshTokenLifetime(14 дн)`.
- `UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableEndSessionEndpointPassthrough()`;
  Development — `DisableTransportSecurityRequirement()`.
- Валидация для собственного admin-API: `AddValidation(o => { o.UseLocalServer(); o.UseAspNetCore(); o.AddAudiences("flow-auth"); })`,
  политика `auth:admin` = scope `auth:admin`.
- CORS для origin'ов клиента (`Cors:Origins`, как в Api): нужен для `POST /connect/token` из браузера и discovery.

### `ClientSeeder` (идемпотентный, при старте)

| client_id | Тип | Разрешения |
|---|---|---|
| `flow-client` | public, PKCE, `ConsentType = Implicit` | authorize/token/endsession; code + refresh; scopes `openid profile email offline_access flow-api`; redirect/post-logout URI из `Auth:Client:RedirectUris/PostLogoutRedirectUris` |
| `flow-api` | confidential, секрет `Auth:ApiClient:Secret` | token; client_credentials; scope `auth:admin` |

### `AuthorizationController`

| Маршрут | Поведение |
|---|---|
| `GET/POST /connect/authorize` | `AuthenticateAsync(Identity.Application)`; нет cookie → `Challenge` на `/account/login` (защита от петли через `TempData`, как в доке OpenIddict 7). Есть → `UserManager.GetUserAsync`; `LockoutEnd` в будущем → `Forbid(access_denied)`; иначе `ClaimsIdentity`: `sub` = Id, `name`, `preferred_username`, `email`; `SetScopes(request.GetScopes())`, `SetResources` по scope; destinations: `name/preferred_username/email` → id token при `profile`/`email`; `SignIn(OpenIddict)`. |
| `POST /connect/token` | refresh grant: перечитать пользователя по `sub`; не найден или lockout → `Forbid(invalid_grant)`; иначе `SignIn` тем же principal. client_credentials: `sub` = client_id, audience `flow-auth`. Code grant OpenIddict обменивает сам. |
| `GET/POST /connect/endsession` | `SignOutAsync(Identity.Application)` + `SignOut(OpenIddict)` с `post_logout_redirect_uri`. |

### `Pages/Account/Login`

Поля «Логин» (username или email — если есть `@`, ищем `FindByEmailAsync`, иначе `FindByNameAsync`), «Пароль», антифоргери.
`SignInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`: `IsLockedOut` → сообщение про блокировку
(постоянная блокировка деактивированных и временная после 5 неудач дают разные тексты по `LockoutEnd`); `Succeeded` →
`SignInAsync(user, isPersistent: false)` → `LocalRedirect(returnUrl)`. Логин-страница выглядит как Flow: тёмная тема, те же токены.

### `AccountsController` (admin-API, `[Authorize(Policy = "auth:admin")]`)

| Маршрут | Тело / ответ |
|---|---|
| `POST /accounts` | `{ id, username, email, password }` → 201 `{ id }`; 409 `{ message }` при занятом username/email (`IdentityResult.Errors` → `DuplicateUserName`/`DuplicateEmail`); 400 на слабый пароль. `Id` задаёт вызывающий (Flow.Api), чтобы профиль и учётная запись совпали без второго запроса. |
| `PATCH /accounts/{id}/username` | `{ username }` → 204 / 409 |
| `PATCH /accounts/{id}/email` | `{ email }` → 204 / 409 |
| `POST /accounts/{id}/password` | `{ currentPassword?, newPassword }`: с `currentPassword` — `ChangePasswordAsync`, без — сброс через `RemovePassword + AddPassword` → 204 / 400 |
| `POST /accounts/{id}/disable` | `SetLockoutEndDateAsync(DateTimeOffset.MaxValue)` + `UpdateSecurityStampAsync` → 204 |
| `POST /accounts/{id}/enable` | `SetLockoutEndDateAsync(null)`, `ResetAccessFailedCountAsync` → 204 |

Кто может сбрасывать чужой пароль, решает Flow.Api (Owner по #15), Auth доверяет `flow-api` целиком.

### `BootstrapUserSeeder` (при старте, после миграций)

Если `Bootstrap:Enabled` (по умолчанию `true`) и `FindByIdAsync(Bootstrap:Id)` пуст: создаёт `ApplicationUser { Id, UserName, Email, EmailConfirmed = true }`,
хеш пишет напрямую (`PasswordHash = hasher.HashPassword(user, Bootstrap:Password)`) и вызывает `CreateAsync(user)` **без** пароля,
поэтому валидаторы пароля не применяются, а уникальность username/email — применяются (конфликт → ошибка старта с понятным текстом).
Если учётная запись с этим Id уже есть — ничего не делает, даже если env поменялся (пароль после первого входа — собственность пользователя).
`Bootstrap:Id` по умолчанию `11111111-1111-1111-1111-111111111111`, одинаковый в Auth и Api.

### Конфигурация `appsettings.json`

```json
{
  "ConnectionStrings": { "Postgres": "Host=localhost;Port=5432;Database=flow_auth;Username=flow;Password=flow" },
  "Cors": { "Origins": [ "http://localhost:5016", "https://localhost:7062" ] },
  "Auth": {
    "Issuer": "http://localhost:5100",
    "Client": {
      "RedirectUris": [ "http://localhost:5016/authentication/login-callback", "https://localhost:7062/authentication/login-callback" ],
      "PostLogoutRedirectUris": [ "http://localhost:5016/authentication/logout-callback", "https://localhost:7062/authentication/logout-callback" ]
    },
    "ApiClient": { "Secret": "dev-only-change-me" },
    "BCrypt": { "WorkFactor": 12 },
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeDays": 14,
    "SigningCertificate": { "Path": null, "Password": null },
    "EncryptionCertificate": { "Path": null, "Password": null }
  },
  "Bootstrap": {
    "Enabled": true,
    "Id": "11111111-1111-1111-1111-111111111111",
    "Username": "admin",
    "Email": "admin@flow.com",
    "Password": "admin"
  }
}
```

`launchSettings.json`: `http://localhost:5100`. В Docker секция `Bootstrap` задаётся env (`Bootstrap__Email`, `Bootstrap__Password`, …),
одинаково для `auth` и `api` через YAML-якорь в compose.

### Тесты — `tests/Flow.Auth.Tests` (WebApplicationFactory + Testcontainers)

- discovery-документ отдаётся, JWKS содержит ключ подписи;
- `POST /accounts` без токена → 401; с client_credentials `flow-api` → 201; повтор username → 409;
- хеш в `AspNetUsers.PasswordHash` начинается с `$2a$12$` (BCrypt), вход с верным паролем проходит, с неверным — нет;
- после старта существует пользователь `Bootstrap:Id` с email/username из конфигурации, вход паролем `admin` (короче политики) проходит; второй старт не создаёт дубликат;
- 5 неверных паролей → lockout, шестой верный отказан до истечения;
- `disable` → страница входа отказывает и refresh grant возвращает `invalid_grant`;
- полный code + PKCE: `HttpClient` с cookie-контейнером проходит `/connect/authorize` → login → callback → `/connect/token` → JWT с `aud = flow-api`, `sub` = Id.

## Flow.Application

- `Abstractions/IActorAccessor` (`Guid? ActorId`) — из #18, источник claims.
- `Abstractions/IAccountService`:

```csharp
public interface IAccountService
{
    Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken ct);
    Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken ct);
    Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken ct);
    Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken ct);
    Task DisableAsync(Guid id, CancellationToken ct);
    Task EnableAsync(Guid id, CancellationToken ct);
}
// AccountResult: Success | Conflict(message) | Invalid(message); недоступность Auth → AuthUnavailableException → 502
```

- `Features/Bootstrap/SeedOwnerCommand(Id, Username, Email, FirstName, LastName)`: если `GetByIdAsync(Id)` пуст — `User.Create(...)` с заданным Id
  (нужна фабрика `User.CreateWithId(id, …)` или `internal` перегрузка для Application), `ChangeRole(Owner)`, `MarkActive()`, `Add`, `SaveChanges`;
  иначе no-op. В Auth **не ходит**: учётную запись с тем же Id сеет сам Flow.Auth. `IAccountService` в бутстрапе не участвует.
  Имя/фамилия по умолчанию «Admin» / «Flow» (`Bootstrap:FirstName/LastName`).
- `UserCreateCommand` (+ `Password`): проверки прав из #18 → `IAccountService.CreateAsync(user.Id, …)` → `Conflict` → `UsernameTaken/EmailTaken` → профиль.
- `UserChangeUsernameCommand`/`UserChangeEmailCommand`: сначала `IAccountService`, потом копия в `Users`.
- `UserChangePasswordCommand(ActorId, UserId, CurrentPassword?, NewPassword)`: сам — с текущим; Owner — сброс без текущего.
- `UserDeactivateCommand`/`UserActivateCommand`: `DisableAsync`/`EnableAsync` перед `user.Deactivate()/Activate()`.
- `UserGetMeQuery(ActorId)`: возвращает профиль, и если `Status == Invited` — `MarkActive()` + `SaveChanges` (единственный запрос с побочным эффектом, задокументировать).
- Тесты: `Fakes/FakeAccountService` (список вызовов + настраиваемый `Conflict`); кейсы: `SeedOwner` создаёт Owner/Active с заданным Id и не трогает `IAccountService`, повтор — no-op; конфликт Auth при `UserCreate` не создаёт профиль;
  смена username: при `Conflict` копия не меняется; деактивация вызывает `Disable` до изменения статуса; `GetMe` переводит Invited → Active один раз.

## Flow.Infrastructure

- `Auth/AuthAccountService : IAccountService` — `HttpClient` на `Auth:BaseUrl`, токен client_credentials через `Auth/ClientCredentialsTokenProvider`
  (кэш до истечения минус 30 с, `POST {Auth}/connect/token`), маппинг 409/400/5xx.
- Регистрация: `services.AddHttpClient<IAccountService, AuthAccountService>()`, `AddSingleton<ClientCredentialsTokenProvider>()`.
- Конфигурация Flow.Api: `Auth:BaseUrl`, `Auth:ApiClient:ClientId = flow-api`, `Auth:ApiClient:Secret`.
- `IUserRepository`: `CountByRoleAsync` из #17; `ExistsByUsername/Email` остаются для локальной страховки. `CountAsync` не нужен: бутстрап идёт по Id.
- Схема `Users` не меняется в этом ТЗ (роль/статус — миграция #17).
- Тесты: без изменений по БД; `AuthAccountService` — юнит-тест с `HttpMessageHandler`-заглушкой на маппинг 409 → `Conflict`.

## Flow.Api

- Пакет `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`.
- `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { Authority = Auth:BaseUrl; Audience = "flow-api"; RequireHttpsMetadata = !Development; MapInboundClaims = false; })`.
- `AddAuthorization(o => o.FallbackPolicy = RequireAuthenticatedUser())` — закрыто всё; `[AllowAnonymous]` только `GET /` (health).
- `Bootstrap/BootstrapOwnerSeeder : IHostedService`: после миграций шлёт `SeedOwnerCommand` из секции `Bootstrap` (та же, что у Auth: `Id`, `Username`, `Email`, `FirstName`, `LastName`; `Password` Api игнорирует). `Bootstrap:Enabled=false` отключает.
- `Auth/ClaimsActorAccessor : IActorAccessor` — `sub` из `HttpContext.User`.
- `UsersController`: `GET /users/me`, `POST /users/{id}/password`; `POST /users` принимает `Password`.
- Ошибки: нет/плохой токен → 401 (JwtBearer); `AuthUnavailableException` → 502 `{ message: "Сервис входа недоступен" }` в общем фильтре (#19); 403 — из `ForbiddenException` (#19).
- Порядок middleware: `UseCors` → `UseAuthentication` → `UseAuthorization` → `MapControllers`.
- Тесты `tests/Flow.Api.Tests`: `WebApplicationFactory<Program>` с `PostgresFixture`; JwtBearer в тестах переключается на локальный ключ
  (`TokenValidationParameters.IssuerSigningKey` + тестовый issuer, `ConfigurationManager` не используется), `IAccountService` → `FakeAccountService`.
  Кейсы: `GET /users` без токена → 401; после старта хоста в `Users` есть Owner с `Bootstrap:Id`, второй старт не дублирует; `GET /users/me` с токеном → профиль и `Invited → Active`.
- `AGENTS.md`: слои (+ `Flow.Auth`), таблица API, раздел «Аутентификация»: как получить токен для ручных запросов (через клиент или `flow-api` client_credentials — только для admin-API, к Flow.Api такой токен не подходит из-за audience).

## Flow.Client

- Пакет `Microsoft.AspNetCore.Components.WebAssembly.Authentication 10.0.0`.
- `wwwroot/appsettings.json`: `ApiBaseUrl`, `AuthBaseUrl`.
- `Program.cs`: `AddOidcAuthentication(o => { Authority = AuthBaseUrl; ClientId = "flow-client"; ResponseType = "code"; DefaultScopes: openid profile email offline_access flow-api; })`;
  `AddHttpClient("FlowApi").AddHttpMessageHandler<FlowAuthorizationMessageHandler>()` (`ConfigureHandler(authorizedUrls: [ApiBaseUrl])`).
- `App.razor`: `CascadingAuthenticationState` + `AuthorizeRouteView`, `NotAuthorized` → `RedirectToLogin`.
- `Pages/Authentication.razor` (`/authentication/{action}`, `RemoteAuthenticatorView`, русские тексты). Экрана setup нет.
- `AppState.CurrentUserId` — из `AuthenticationStateProvider` (`sub`); выбор «Это я», `flow.me` в localStorage и `SetCurrentUser` из UI удаляются.
  `Sidebar`: футер — текущий пользователь, «Выйти» → `authentication/logout`.
- `FlowApi`: 401 → редирект на вход (`AccessTokenNotAvailableException.Redirect()`), 403 → тост, 502 → тост «Сервис входа недоступен».
- `UserPage`: карточка «Пароль» (свой — текущий + новый; Owner у чужого — «Сбросить пароль»). `CreateUserDialog`: «Начальный пароль» + «Сгенерировать» + «Скопировать».

## Инфраструктура репозитория

- `Flow.slnx`: `src/Flow.Auth`, `tests/Flow.Auth.Tests`, `tests/Flow.Api.Tests`.
- `docker-compose.yml`: сервис `auth` (порт 5100, `ConnectionStrings__Postgres` на `flow_auth`, `Auth__*`, volume с PFX);
  `api` получает `Auth__BaseUrl=http://auth:5100` и `Auth__ApiClient__Secret`. Базовый пользователь — общий блок env через якорь:

```yaml
x-bootstrap: &bootstrap
  Bootstrap__Id: ${BOOTSTRAP_ID:-11111111-1111-1111-1111-111111111111}
  Bootstrap__Username: ${BOOTSTRAP_USERNAME:-admin}
  Bootstrap__Email: ${BOOTSTRAP_EMAIL:-admin@flow.com}
  Bootstrap__Password: ${BOOTSTRAP_PASSWORD:-admin}
  Bootstrap__FirstName: ${BOOTSTRAP_FIRST_NAME:-Admin}
  Bootstrap__LastName: ${BOOTSTRAP_LAST_NAME:-Flow}
services:
  auth:
    environment: { <<: *bootstrap, ... }
  api:
    environment: { <<: *bootstrap, ... }
```

  Postgres — init-скрипт `docker/postgres/init.sql`
  с `CREATE DATABASE flow_auth`. Issuer в токене должен совпадать с тем, что видит браузер, поэтому в compose `Auth__Issuer`
  — внешний адрес (`http://localhost:5100`), а `api` валидирует по нему же через `Auth__BaseUrl` для discovery
  (внутренний адрес) + `TokenValidationParameters.ValidIssuer` = внешний. Это зафиксировать в `AGENTS.md`.
- `Dockerfile.auth` (копия текущего с другим стартовым проектом) и `docker.yml` — второй образ.
- `build.yml` собирает `Flow.slnx` целиком — новые проекты подхватываются; интеграционные тесты Auth тоже на Testcontainers.
- Локально: `POSTGRES_PORT=5433`, обе строки подключения с 5433 (см. заметку про занятый 5432).

## Влияние на ТЗ ролей (#15)

| Было | Стало |
|---|---|
| `X-Actor-Id`, middleware `ActorProvider` (#19) | `ClaimsActorAccessor` из `sub`; заголовок не существует |
| Первый пользователь → Owner внутри `UserCreateCommand` | Базовый Owner сеется при старте из `Bootstrap:*`; `UserCreateCommand` всегда требует actor ≥ Admin и `Password` |
| `Invited → Active` при правке профиля | при первом `GET /users/me` |
| Деактивация = статус в `Users` | + `IAccountService.DisableAsync` (lockout в Identity) до смены статуса |
| Смена username/email локально | сначала Auth (Identity — источник истины), потом копия |
| Клиент выбирает «Это я» (#20) | удаляется; «я» = `sub` |

Порядок: **Auth-1…4 → #16/#17 (можно параллельно с Auth) → #18/#19 → #20.**

## Вне этого ТЗ

- Приглашения по email, восстановление пароля, подтверждение email (SMTP).
- Внешние провайдеры (GitHub/Google) через OpenIddict client-stack в Flow.Auth.
- Отзыв живых access token'ов при деактивации (короче срок или introspection).
- Компенсация «учётная запись создана, профиль нет» при `UserCreate` (`DeleteAsync` в admin-API) и outbox для вызовов Auth.
- Очистка истёкших токенов (`OpenIddict.Quartz`).
- Роли Identity, 2FA, роль в токене.

## Подзадачи (предлагаемые issues)

1. **Auth-1 Flow.Auth: сервис** — проект, `AuthDbContext`, Identity + BCrypt, OpenIddict server, `ClientSeeder`, `AuthorizationController`, страница входа, admin-API, миграция, `Flow.Auth.Tests`, `Dockerfile.auth`, compose, CI.
2. **Auth-2 Application + Infrastructure** — `IAccountService`, `AuthAccountService`, `SeedOwnerCommand`, `UserChangePassword`, правки Users-команд, тесты с `FakeAccountService`.
3. **Auth-3 Flow.Api** — JwtBearer, fallback-policy, `ClaimsActorAccessor`, `BootstrapOwnerSeeder`, `GET /users/me`, 502-маппинг, `Flow.Api.Tests`, `AGENTS.md`.
4. **Auth-4 Flow.Client** — OIDC-вход, «Выйти», удаление «Это я», пароль в профиле и модалке.

Порядок: 1 → 2 → 3 → 4. PR: Auth-1 отдельно (ничего не ломает), Auth-2 отдельно (Api ещё открыт, но команды уже ходят в Auth — для локального запуска нужен поднятый Flow.Auth), Auth-3 + Auth-4 вместе (с Auth-3 API закрывается).
