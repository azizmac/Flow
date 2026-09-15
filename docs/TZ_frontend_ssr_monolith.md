# ТЗ: фронт на SSR — последний шаг к монолиту

Статус: **черновик на согласование**. Код не менялся — документ описывает решение и порядок работ.

База — ветка `AuthModularMonolith` (`docs/TZ_modular_monolith.md`, коммит `c21cc0b`), а не `main`.
Порядок работ: сначала мержится модульный монолит, затем это ТЗ. Ниже «сейчас» означает состояние той ветки.

## Исходное требование

Свести приложение в монолит и перевести фронтенд с клиентского рендеринга (Blazor WebAssembly) на
серверный: HTML собирает сервер, браузер получает готовую разметку, а не `.wasm`-рантайм, который
потом сам ходит за данными.

## Где мы уже находимся

Бэкенд **уже монолит**: ветка `AuthModularMonolith` превратила `Flow.Auth` из отдельного сервиса
в Razor Class Library и подключила его к хосту `Flow.Api` через `AddAuthModule(...)`. Из этого следует:

- один backend-процесс, одна база `flow`, Auth-модуль живёт в схеме `auth`;
- HTTP между своими сервисами больше нет: `IAccountService` реализован in-process
  (`Security/AuthAccountManager` поверх `UserManager`), admin-API `/accounts`, клиент `flow-api`,
  `ClientCredentialsTokenProvider` и маппинг 502 удалены;
- токены валидируются локально (`OpenIddict.Validation.UseLocalServer()`) — ни discovery, ни JWKS,
  ни `Auth:BaseUrl` больше нет;
- **cookie-схема уже настроена и работает**: `flow.auth`, `IdentityConstants.ApplicationScheme`, 8 часов
  со скользящим продлением; её ставит страница `/account/login`, а `/connect/authorize` её читает;
- контейнеров приложения два: `api` и `client`.

Незакрытым остался ровно один шов — фронт:

- `Flow.Client` собирается в статику и отдаётся отдельным nginx-контейнером;
- `docker/client/entrypoint.sh` пишет `wwwroot/appsettings.json` с адресами бэкенда для браузера;
- вход — OIDC code + PKCE **в браузере** (`AddOidcAuthentication`, публичный клиент `flow-client`),
  access token лежит в `sessionStorage`;
- данные — `Services/FlowApi` (293 строки HTTP-клиента) к своему же процессу;
- из-за разных origin в хосте живёт CORS-политика `FlowClient` (`Cors:Origins`).

## Проблема

- **Холодный старт.** Первый заход тянет рантайм .NET и сборки; в HTML нет ничего, кроме
  `<div id="app">` со спиннером.
- **Каскад запросов.** Рантайм → токен → `GET /users/me` → `GET /users` → данные страницы,
  каждый шаг — отдельный round-trip из браузера.
- **Токен в браузере.** Access token в `sessionStorage` доступен любому скрипту на странице.
- **HTTP к самому себе.** Страница вызывает эндпоинт своего же процесса: DTO → сериализация → HTTP →
  десериализация → MediatR-хендлер. Ровно тот путь, который модульный монолит убрал между Api и Auth,
  но оставил между UI и ядром.
- **Разные origin ради ничего.** CORS, `appsettings.json` из entrypoint, отдельный образ с nginx,
  внешний redirect URI в конфиге OpenIddict — всё это существует только потому, что фронт на другом порту.

## Главное следствие для аутентификации

В `docs/TZ_modular_monolith.md` записано: *«Cookie-аутентификация браузера не делается. Вопрос отложен:
куки требуют same-origin и не закрывают задачу машинных клиентов»*.

**SSR снимает первую половину возражения полностью**: фронт и бэкенд оказываются на одном origin,
и cookie становится естественным способом хранить сессию. Вторая половина остаётся в силе и не мешает:
OpenIddict никуда не девается и продолжает обслуживать машинных клиентов и внешний API — просто браузеру
он больше не нужен.

То есть SSR — это не «ещё одна задача рядом», а недостающее условие для отложенного решения.

## Границы

| | В ТЗ | Вне ТЗ |
|---|---|---|
| Фронтенд | Перевод существующих экранов на серверный рендеринг, вёрстка и поведение 1:1 | Редизайн, новые экраны, канбан, мобильная вёрстка |
| Хостинг | Страницы переезжают в тот же хост `Flow.Api`, один контейнер приложения | Переименование хоста, вертикальный сплит ядра, Kubernetes |
| Аутентификация | Cookie-сессия для страниц на том же origin; OpenIddict остаётся для API и машинных клиентов | Смена протокола, отказ от OpenIddict, SSO, приглашения и восстановление пароля |
| Доступ к данным | Замена HTTP-вызовов из UI на прямой вызов Flow.Application (MediatR) | Изменение контрактов, схемы БД, бизнес-правил и матрицы прав |
| Инфраструктура | compose, Dockerfile, CI, README/AGENTS | CDN, TLS-терминатор, масштабирование на несколько узлов |

Матрица прав, журнал задачи, Markdown-редактор, роли и статусы не меняются: это перенос того же
поведения на другой способ рендеринга.

## Целевая архитектура

```
Flow.Api — единственный процесс приложения
 ├─ Flow.Auth (RCL): Identity + BCrypt, OpenIddict, /connect/*, /account/login, /account/change-password
 ├─ Components/: страницы и компоненты из Flow.Client (Blazor Web App, InteractiveServer)
 ├─ Controllers/: REST API под префиксом /api (машинные клиенты, Bearer)
 ├─ ядро: Domain / Application / Infrastructure
 └─ wwwroot/: tokens.css, app.css, flow.js, иконки
Docker: app (+ стек данных без изменений)
```

Хост остаётся `Flow.Api` — решение «имя проекта не меняется» уже принято в модульном монолите,
и переименование потянуло бы за собой тесты, Dockerfile, CI и `.run`. Выделять UI в отдельный RCL
(по образцу Auth) не нужно: UI не доменный модуль, он потребитель всех остальных.

### Режим рендеринга

Рекомендуется **Blazor Web App с глобальным `InteractiveServer`** и статическим SSR там, где страница
не нуждается в интерактивности на первом кадре.

| Вариант | Оценка |
|---|---|
| **InteractiveServer (рекомендуется)** | Страницы переносятся почти без правок: те же события, те же `@bind`, тот же DI. Даёт и SSR первого ответа, и интерактивность без WASM. Минус — постоянное соединение и состояние на сервере |
| Static SSR + интерактивные острова | Самый дешёвый рантайм, но фильтры, поповеры, дровер и редактор переписываются на формы и острова. Дороже, чем весь остальной перенос |
| InteractiveAuto (сервер, затем WASM) | Два рантайма и два DI-контейнера; код обязан работать в обоих, прямой вызов MediatR из компонентов невозможен. Противоречит цели |
| Hosted WASM с prerendering | Решает только первый экран: `.wasm` грузится, токен остаётся в браузере. Половина выгоды за 80 % работы |
| Другой стек (React/Next.js, htmx) | Второй язык и вторая сборка в проекте на одном .NET. В этом ТЗ не рассматривается |

Начинаем с `InteractiveServer` глобально, затем точечно переводим в статический SSR то, что этого
не требует. Обратимо на уровне одного атрибута.

### Аутентификация страниц

**Страницы — cookie, без OIDC-редиректа в браузере.** Схема `flow.auth` уже существует, страница
`/account/login` уже ставит её через `SignInManager.SignInAsync`, `/account/change-password` уже
защищена политикой `AuthConstants.CookiePolicy`. Нужно лишь распространить эту cookie на приложение.

- **Выбор схемы.** Сейчас `DefaultAuthenticateScheme` — валидация OpenIddict (Bearer). Добавляется
  policy-схема: есть заголовок `Authorization` или путь начинается с `/api` → Bearer, иначе — cookie.
  `FallbackPolicy = RequireAuthenticatedUser` сохраняется, `[AllowAnonymous]` остаётся у `/account/login`,
  `/connect/*` и health.
- **Вход/выход.** Вход — существующая страница `/account/login` с `ReturnUrl`; cookie-схеме указывается
  `LoginPath`, и неаутентифицированный заход на `/boards` сам уводит туда. Выход — `SignOutAsync`
  cookie-схемы (эндпоинт `/account/logout`), редирект на вход. Браузерного `/connect/authorize`
  в обычном сценарии больше нет.
- **«Запомнить меня».** `SignInAsync(user, isPersistent:)` сейчас всегда `false` — сессия до закрытия
  браузера. Можно добавить чекбокс и persistent-cookie на 14 дней; параметры (`ExpireTimeSpan`,
  `SlidingExpiration`, `HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` для http в compose)
  фиксируются в `AddAuthModule`. Ключи Data Protection уже переживают рестарт (`DataProtection:KeysPath`,
  том `auth-keys`) — cookie после перезапуска контейнера остаётся валидной.
- **Обязательная смена пароля.** Сейчас флаг `MustChangePassword` перехватывается в двух местах:
  на `/account/login` и в `/connect/authorize`. Второй путь из браузерного сценария уходит, поэтому
  нужен guard на страницах приложения: пока флаг стоит — редирект на `/account/change-password`.
  Иначе пользователь с флагом попадёт в приложение по одной только cookie.
- **Деактивация при живой cookie.** Проверку `IsLockedOut`, которая была в `/connect/authorize`,
  переносим в `CookieAuthenticationEvents.OnValidatePrincipal`: деактивированный теряет сессию
  на первом же запросе. Ядро и так проверяет статус (`ActorResolver`), но его 401 на SSR-странице
  должен превращаться в выход и редирект на вход, а не в экран ошибки.
- **OpenIddict остаётся** — для машинных клиентов и внешнего API. Публичный клиент `flow-client`
  с redirect URI на WASM-колбэки (`/authentication/login-callback`) удаляется из `ClientSeeder`
  и compose: браузерного OIDC-клиента больше нет.
- **Что удаляется на клиенте:** `AddOidcAuthentication`, `Pages/Authentication.razor`,
  `RemoteAuthenticatorView`, `RedirectToLogin`, `FlowAuthorizationMessageHandler`,
  `AuthenticationService.js`, пакет `…WebAssembly.Authentication`.

Альтернатива — оставить OIDC, но гонять code + PKCE серверным handler'ом к самому себе. Работает,
однако это редирект-танец процесса с самим собой ради токена, который потом никому не нужен:
в ядро мы ходим напрямую. Отвергнуто.

### Actor (кто выполняет действие)

`Auth/ClaimsActorAccessor` читает `sub`, а при его отсутствии — `ClaimTypes.NameIdentifier`. Identity
кладёт в cookie именно `NameIdentifier`, поэтому **под cookie accessor работает без правок** — это удача,
а не совпадение, проверить тестом.

Правка нужна другая: в интерактивном серверном рендеринге `HttpContext` существует только на первом
ответе. Нужна вторая реализация `IActorAccessor` поверх `AuthenticationStateProvider` (scoped на circuit),
с тем же `UnauthorizedActorException` при отсутствии `sub`. Контроллеры остаются на нынешней.
Без этого любое действие после первого рендера упадёт.

### Доступ к данным

`Services/FlowApi` превращается в интерфейс `IFlowGateway` с теми же сигнатурами и тем же `ApiResult<T>` —
тогда ~3 500 строк Razor не правятся вообще. Реализация `MediatorFlowGateway` вызывает `IMediator.Send`
и раскладывает `ForbiddenException` / `UnauthorizedActorException` в тот же `ApiResult`
(`Forbidden`, `Unauthorized`), что раньше давал HTTP-слой. `AuthUnavailableException` и ветка 502
уже удалены модульным монолитом — их в маппинге нет.

HTTP-реализация остаётся на время миграции и удаляется в конце: она же страховка на случай отката.

### Конфликт маршрутов — обязателен префикс `/api`

Маршруты контроллеров и страниц совпадают буквально:

| Страница | Контроллер |
|---|---|
| `/boards`, `/boards/{id:guid}` | `GET /boards`, `GET /boards/{id:guid}` |
| `/tasks/{id:guid}` | `GET /tasks/{id:guid}` |
| `/users`, `/users/{id:guid}` | `GET /users`, `GET /users/{id:guid}` |

В одном хосте это не разойдётся. Контроллеры переезжают под `/api` (`MapGroup("/api")` либо конвенция).
Правятся `Flow.Api.Tests` (пути) и HTTP-реализация gateway. Маршруты Auth-модуля (`/account/*`,
`/connect/*`) с маршрутами страниц не пересекаются. **Этот пункт можно сделать прямо сейчас** (этап 0).

### Статика и JS

- `css/tokens.css`, `css/app.css`, `js/flow.js`, иконки переезжают в `wwwroot` хоста (там уже лежит
  статика Auth-модуля); `index.html` заменяется на `Components/App.razor` (`<HeadOutlet>`, `<Routes>`).
- Отдача статики — `MapStaticAssets()`: fingerprint, предсжатие и заголовки кэша из коробки.
  `docker/client/nginx.conf` и `entrypoint.sh` уходят целиком.
- Razor Pages Auth-модуля и Blazor-компоненты в одном хосте сосуществуют штатно:
  `MapRazorPages()` + `MapRazorComponents<App>()`. Переводить страницу входа на Blazor не нужно.
- **JS-интероп только в `OnAfterRenderAsync`**: на пререндере `IJSRuntime` недоступен. Под аудит —
  `flow.copy`, `flow.focus`, `flow.rect`, `flow.storageGet/Set`, `flow.registerHotkeys`, `flow.editor.*`.
- `Components/MarkdownEditor`: привязка по `onchange` вместо `oninput`, автодополнение `@` с debounce —
  иначе каждое нажатие уходит на сервер. Рендер Markdown (Markdig) переезжает на сервер.
- Анимации остаются CSS-only — задержка канала на них не влияет.

### Инфраструктура

- `docker-compose.yml`: сервисы `api` и `client` заменяются одним `app` (порт `${APP_PORT:-5016}`).
  Уходят `API_BASE_URL`, `AUTH_BASE_URL`, `Cors__Origins__0`, `Auth__Client__RedirectUris__0`,
  `Auth__Client__PostLogoutRedirectUris__0`, переменная `CLIENT_PORT`. `Auth__Issuer` остаётся —
  он нужен токенам машинных клиентов и теперь совпадает с адресом приложения.
- Тома `auth-certs` и `auth-keys` остаются; `auth-keys` становится критичнее — на этих ключах
  подписана cookie сессии.
- `src/Flow.Client/Dockerfile`, `docker/client/` удаляются; CI собирает один образ вместо двух
  (`CLIENT_IMAGE_NAME` уходит).
- README (оба языка), `AGENTS.md`, `.run/`, `Flow.slnx` обновляются: адрес приложения один.
- Стек данных (`docker-compose.data.yml`) не трогается.

## Состав работ по этапам

### Этап 0. Подготовка — можно делать сразу, до миграции

1. Префикс `/api` для контроллеров + правка `FlowApi` и `Flow.Api.Tests`.
2. Аудит JS-интеропа: ни одного вызова `flow.*` из `OnInitializedAsync`.
3. `Services/FlowApi` → интерфейс `IFlowGateway` + нынешняя HTTP-реализация; страницы инжектят интерфейс.

После этапа 0 система работает как сейчас, но готова к переносу; проверяется существующими тестами.

### Этап 1. Страницы в хосте

4. Blazor Web App в `Flow.Api`: `AddRazorComponents().AddInteractiveServerComponents()`,
   `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
5. Перенос компонентов и страниц из `Flow.Client` в `Components/`, статика в `wwwroot`,
   `App.razor` / `Routes.razor` вместо `index.html`.
6. Проверка вёрстки под временной аутентификацией (существующая Bearer-схема).

### Этап 2. Cookie-сессия

7. Policy-схема выбора (cookie для страниц, Bearer для `/api`), `LoginPath`, `/account/logout`.
8. Guard `MustChangePassword` на страницах приложения; `OnValidatePrincipal` для деактивированных.
9. `IActorAccessor` поверх `AuthenticationStateProvider` для circuit'ов.
10. Удаление WASM-обвязки OIDC и клиента `flow-client` из `ClientSeeder`/compose.
11. «Запомнить меня» (опционально, решается вместе с параметрами cookie).

### Этап 3. Прямой доступ к данным

12. `MediatorFlowGateway` + маппинг доменных исключений в `ApiResult`.
13. Переключение DI, удаление HTTP-реализации и HttpClient-инфраструктуры клиента.

### Этап 4. Оптимизация рендеринга

14. Статический SSR там, где интерактивность не нужна на первом кадре (`/boards`, `/users`,
    `/users/{id}`), при необходимости `@attribute [StreamRendering]`.
15. `MarkdownEditor`: `onchange` и debounce.

### Этап 5. Инфраструктура и документация

16. Один сервис в `docker-compose.yml`, правка `.env.example`, `.run/`, Dockerfile.
17. CI: один образ приложения вместо двух.
18. README, README.ru, `AGENTS.md`, `Flow.slnx`; удаление `src/Flow.Client` и `docker/client`.

### Этап 6. Тесты

19. `Flow.Api.Tests` — те же кейсы на путях `/api/*`; cookie-сценарии рядом с существующими
    `Flow.Auth.Tests` (`LoginTests`, `PasswordChangePolicyTests`), которые уже ходят через
    `WebApplicationFactory<Program>` того же хоста.
20. Новое: SSR-смоук (`GET /boards` без cookie → редирект на `/account/login`; с cookie → 200 и названия
    проектов **в HTML первого ответа**); `MustChangePassword` не пускает на страницы приложения;
    деактивация гасит cookie; маппинг исключений в `MediatorFlowGateway` (403/401).
21. По желанию — bUnit на `Permissions`, `TaskRow`, `DueChip`.

Этапы 0–3 обязательны и идут по порядку. 4 можно отложить, 5–6 закрываются вместе с 3.

## Критерии приёмки

- `curl` на `/boards` с валидной cookie возвращает HTML **со списком проектов внутри**; без cookie —
  редирект на `/account/login`.
- В ответе нет ни одного `.wasm`; `_framework/blazor.web.js` — единственный рантайм-скрипт.
- Контейнер приложения один; образа `client` и nginx-конфига нет.
- В браузере нет ни access, ни refresh токена; сессия — одна `HttpOnly`-cookie.
- CORS-политики в приложении нет, `Cors:Origins` удалён.
- Публичный клиент `flow-client` из OpenIddict удалён; `/connect/*` остаются рабочими для машинных клиентов.
- Сценарии из README проходят руками: вход, обязательная смена начального пароля, создание проекта
  и задачи, назначение, срок, комментарий с `@упоминанием`, журнал, деактивация, выход.
- `dotnet test Flow.slnx` зелёный; CI собирает и публикует один образ приложения.

## Что мы теряем и чем это закрываем

| Риск | Чем закрываем |
|---|---|
| Постоянное соединение: обрыв сети — «Пытаемся восстановить соединение» вместо работы | Штатный reconnect-UI Blazor, стилизованный под тему; действия идемпотентны на уровне API |
| Задержка канала на каждое действие (ввод, поповеры, фильтры) | Приложение внутреннее; ввод и подсказки — локально/с debounce; анимации на CSS |
| Состояние circuit'а в памяти сервера: расход памяти, sticky sessions при нескольких узлах | Узел один. Многоузловой сценарий — отдельное ТЗ (sticky на балансировщике / Azure SignalR) |
| Потеря ключей Data Protection разлогинивает всех | Том `auth-keys` уже есть; в бэкап-скрипты добавляется предупреждение |
| Ломается совместимость REST API из-за префикса `/api` | Внешних потребителей нет; путь фиксируется в README и `AGENTS.md` до появления первого |
| Работа офлайн и PWA становятся невозможны | Их и сегодня нет: клиент без бэкенда бесполезен |

## Открытые вопросы

1. **Машинные клиенты — когда?** Если клиент-робот появится раньше SSR, у него свой confidential-клиент
   и client_credentials; на браузерную cookie это не влияет. Уточнить сроки — от них зависит,
   держать ли `/connect/*` под нагрузочным вниманием.
2. **Где работают люди — локальная сеть или интернет?** От этого зависит, достаточно ли
   `InteractiveServer` или этап 4 (острова на статическом SSR) становится обязательным.
3. **Ожидаемое число одновременных пользователей** — лимиты circuit'ов и память контейнера.
4. **«Запомнить меня» нужен?** Сейчас сессия живёт до закрытия браузера (`isPersistent: false`).
5. **Нужен ли откат на WASM?** Если да — `Flow.Client` не удаляем на этапе 5, а держим до конца
   испытательного периода ценой двух сборок.

## Навигация

- Модульный монолит (база этого ТЗ) — `docs/TZ_modular_monolith.md`
- Аутентификация (исходное сервисное ТЗ) — `docs/TZ_auth.md`
- Роли и права — `docs/TZ_user_roles.md`
- Лента задачи и Markdown-редактор — `docs/TZ_task_activity_comments.md`
- Инфраструктура и стек данных — `docs/TZ_infra_data_split.md`
