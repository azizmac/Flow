# ТЗ: E2E-тесты на Playwright (tests/Flow.E2E.Tests)

## Исходное требование

Нужны сквозные (end-to-end) тесты на **Playwright**: браузер поднимает настоящий стек Flow
(Postgres + MinIO + Flow.Auth + Flow.Api + Flow.Client), проходит пользовательские сценарии
так же, как человек, и падает, если сломался вход, права, создание задачи или живой UI.

Текущая пирамида заканчивается на интеграционных тестах уровня HTTP (`Flow.Api.Tests`,
`Flow.Auth.Tests`): связка «Blazor WASM → токен → API → Postgres» не проверяется ничем,
а именно в ней живут регрессии вида «поповер уехал от якоря», «после logout вернулись на
закрытую страницу», «403 не превратился в тост».

Область ТЗ: новый тестовый проект `tests/Flow.E2E.Tests`, оверрайд compose для изолированного
окружения, атрибуты `data-testid` в `Flow.Client` (и в двух Razor-страницах `Flow.Auth`),
отдельная джоба CI. Продуктовый код меняется **только** добавлением `data-testid` — поведение
и разметка не переписываются под тесты.

## Принятые решения

- **Playwright для .NET + xUnit** (`Microsoft.Playwright.Xunit`), а не Node `@playwright/test`.
  Причина: в репозитории один тулчейн — `dotnet test Flow.slnx`, xUnit во всех пяти тестовых
  проектах, DTO берутся из `Flow.Shared` (сидирование данных по API — типизированное, без дублей
  контрактов в JS). Цена решения: нет встроенных `retries`, UI-mode и шардинга — компенсируется
  правилами из секции «Стабильность». Если E2E вырастет до сотен сценариев и понадобится шардинг,
  переезд на Node рассматривается отдельно; POM-слой к тому моменту уже описан и переносим.
- **Стек под тесты поднимается `docker compose`**, не Testcontainers. Нужен клиент, собранный
  и отданный nginx (Blazor WASM), плюс три сервиса и две базы — это ровно `docker-compose.yml`,
  который уже есть. Дублировать его в C# нельзя: разъедется.
- **Изолированное окружение**: отдельный проект compose `flow-e2e` и отдельные порты
  (`docker-compose.e2e.yml`), чтобы E2E не гасил локальную разработку на 5016/8080/5100/5432.
- **Тесты не запускают compose сами** (кроме локального опционального режима): окружение
  поднимает шаг CI / скрипт, тесты получают адреса через переменные окружения. Так тот же проект
  гоняется против dev-стенда и, позже, против staging.
- **Один браузер — Chromium.** Firefox/WebKit не добавляем: клиент не обещает их поддержку,
  а тройная матрица утраивает время и флак.
- **Вход проходится один раз** в глобальной подготовке, дальше переиспользуется cookie-сессия
  Flow.Auth через `storageState` (см. «Аутентификация в тестах»).
- **Селекторы — `data-testid`**, не текст и не CSS-классы. Интерфейс русский, тексты и
  DRESSY-классы меняются на каждом UI-полировочном PR; `data-testid` — контракт.
- **Данные тесты создают сами** и удаляют за собой; общий фикстурный «демо-набор» не сидируем.
  Единственные общие данные — bootstrap-Owner из compose.
- **E2E не заменяет юнит-тесты.** Матрицы прав проверяются в `PermissionTests`; в E2E
  по одному сценарию на роль — что UI действительно скрывает кнопку и что сервер отдаёт 403.

## Границы: что проверяет E2E, а что нет

| Уровень | Где проверяется | Что именно |
|---|---|---|
| Инварианты домена | `Flow.Domain.Tests` | Board/TaskItem/User, роли и статусы |
| Права, фичи | `Flow.Application.Tests` (`PermissionTests`) | вся матрица ролей, по тесту на строку |
| HTTP-контракты, токены | `Flow.Api.Tests`, `Flow.Auth.Tests` | 401/403/409/502, code+PKCE, lockout |
| **Пользовательские пути** | **`Flow.E2E.Tests`** | вход → работа в UI → данные в БД; поведение живого UI |

Не входит в ТЗ: визуальная регрессия (скриншот-дифы), мобильная эмуляция, нагрузочные тесты,
проверка писем, тесты против production.

## Окружение под тесты

`docker-compose.e2e.yml` — оверрайд к основному файлу, меняет только порты, имя bootstrap-пароля
и адреса redirect. Запуск:

```bash
docker compose -p flow-e2e -f docker-compose.yml -f docker-compose.e2e.yml up -d --build --wait
```

| Сервис | Порт наружу (dev → e2e) |
|---|---|
| Postgres | 5432 → **5434** |
| MinIO api/console | 9000/9001 → **9010/9011** |
| Flow.Auth | 5100 → **5101** |
| Flow.Api | 8080 → **8081** |
| Flow.Client | 5016 → **5017** |

Что обязательно переопределяется в оверрайде (иначе вход сломается на redirect_uri):

- `AUTH_ISSUER=http://localhost:5101` (в auth и в api — issuer один и тот же, внешний);
- `Auth__Client__RedirectUris__0=http://localhost:5017/authentication/login-callback`
  и `PostLogoutRedirectUris__0=…/logout-callback`;
- `Cors__Origins__0=http://localhost:5017` в auth и api;
- клиенту `API_BASE_URL=http://localhost:8081`, `AUTH_BASE_URL=http://localhost:5101`
  (`docker/client/entrypoint.sh` пишет из них `wwwroot/appsettings.json`);
- `BOOTSTRAP_PASSWORD=${E2E_BOOTSTRAP_PASSWORD:-admin}` — оставляем короткий `admin`,
  чтобы тест обязательной смены пароля был честным.

`--wait` ждёт healthcheck'и Postgres и MinIO, но у auth/api их нет: добавить обоим
`healthcheck` на `GET /` (api) и `GET /.well-known/openid-configuration` (auth) — иначе
первый тест ловит гонку со стартом миграций и сидеров. Это часть работ по ТЗ.

Переменные, которые читают тесты (значения по умолчанию — порты e2e из таблицы):

| Переменная | Смысл |
|---|---|
| `E2E_CLIENT_URL` | базовый адрес Blazor-клиента |
| `E2E_API_URL` | базовый адрес Flow.Api (для сидирования и ассертов по API) |
| `E2E_AUTH_URL` | базовый адрес Flow.Auth |
| `E2E_OWNER_LOGIN` / `E2E_BOOTSTRAP_PASSWORD` / `E2E_OWNER_PASSWORD` | bootstrap-Owner: логин, начальный пароль, пароль после обязательной смены |
| `E2E_HEADED` | `1` — запуск с окном и `SlowMo` для отладки |

## Структура проекта

```
tests/Flow.E2E.Tests/
  Flow.E2E.Tests.csproj          Microsoft.Playwright.Xunit, xunit, ссылка на Flow.Shared
  E2E.cs                         адреса и креды из env + значения по умолчанию
  Fixtures/
    StackFixture.cs              ICollectionFixture: проверка живости стека, глобальный вход, storageState
    FlowPageTest.cs              базовый класс: PageTest + storageState + трассировка + артефакты
    FlowApiClient.cs             APIRequestContext с Bearer: сидирование и очистка данных, ассерты по API
    TestData.cs                  уникальные Key проекта (^[A-Z][A-Z0-9]{1,9}$), username, email
  Pages/                         Page Object Model — по одному классу на экран
    LoginPage.cs  ChangePasswordPage.cs  BoardsPage.cs  BoardPage.cs
    TaskDrawer.cs  TaskDetailsPage.cs  UsersPage.cs  UserProfilePage.cs  SidebarComponent.cs
  Scenarios/
    AuthTests.cs  BoardsTests.cs  TasksTests.cs  AssignmentTests.cs
    UsersTests.cs  PermissionsTests.cs  UiBehaviourTests.cs
```

Правила слоя POM: класс инкапсулирует только локаторы и действия (`CreateBoardAsync(name, key)`),
ассертов внутри нет; каждый метод оставляет страницу в предсказуемом состоянии; ожидания —
через `Expect(...)`, не через `Wait*` c таймаутами.

Проект добавляется в `Flow.slnx` в папку `/tests/`. `docker-compose.e2e.yml` — в папку `/docker/`.

## Аутентификация в тестах

Три ловушки, из которых вырастает вся схема:

1. **Токен Blazor лежит в `sessionStorage`**, а `storageState` Playwright сохраняет только
   cookies и `localStorage`. Значит «залогиниться один раз и переиспользовать токен» напрямую нельзя.
2. Зато **cookie-сессия Flow.Auth** в `storageState` сохраняется. С живой cookie клиентский
   редирект на `/connect/authorize` возвращает код **без формы входа** — тест стартует уже
   авторизованным, форму проходит только `AuthTests`.
3. **Bootstrap-пользователь обязан сменить пароль** (`MustChangePassword`, #26): после первого
   входа Flow.Auth ведёт на `Pages/Account/ChangePassword` и кода не выдаёт.

Глобальная подготовка (`StackFixture`, один раз на прогон):

1. дождаться `GET {E2E_AUTH_URL}/.well-known/openid-configuration` и `GET {E2E_API_URL}/` (ретраи до 60 с);
2. открыть `{E2E_CLIENT_URL}`, пройти вход `E2E_OWNER_LOGIN` / `E2E_BOOTSTRAP_PASSWORD`;
3. **идемпотентно**: если показалась страница смены пароля — сменить на `E2E_OWNER_PASSWORD`;
   если нет (том БД переиспользован, пароль уже сменён) — войти сразу с `E2E_OWNER_PASSWORD`;
4. дождаться загрузки клиента (`GET /users/me` отработал — в сайдбаре появился профиль);
5. сохранить `storageState` в `bin/.../e2e-auth.json` — его подхватывает `FlowPageTest`.

`FlowApiClient` достаёт access token из `sessionStorage` страницы (ключ `oidc.user:…` библиотеки
`AuthenticationService.js`, поле `access_token`) и передаёт его как Bearer в `APIRequestContext`.
Логика извлечения живёт **в одном методе** — смена версии OIDC-библиотеки ломает одно место.
Токен `client_credentials` клиента `flow-api` для Flow.Api не годится: у него `aud = flow-auth`.

## Изоляция данных

- Каждый тест создаёт свои сущности с уникальными идентификаторами: `TestData.BoardKey()` →
  `E` + 5 символов base36 (влезает в `^[A-Z][A-Z0-9]{1,9}$`), username → `e2e-<short>`,
  email → `e2e-<short>@flow.test`.
- Удаление за собой — в `IAsyncLifetime.DisposeAsync` через `FlowApiClient` (`DELETE /boards/{id}`
  сносит задачи каскадом). Пользователей API не удаляет: тесты людей **деактивируют** созданных
  и живут с тем, что их накапливается — списки фильтруются по уникальному префиксу.
- Owner из bootstrap не трогаем: ни роль, ни статус, ни пароль (кроме шага глобальной подготовки).
  Последнего Owner нельзя понизить — тест, который это нарушит, развалит весь прогон.
- Тесты в одном классе идут последовательно (xUnit-дефолт), классы параллелятся. Классы,
  которые меняют общие списки людей (`UsersTests`, `PermissionsTests`), объединяются в одну
  xUnit-коллекцию и параллельно не идут.
- Прогон в CI всегда на чистых томах (`down -v` перед `up`) — база детерминированная.

## Контракт `data-testid`

Добавляется в продуктовый код. Правило именования: `<экран-или-компонент>-<элемент>`,
для повторяющихся строк — плюс `data-testid` на контейнере и естественный дочерний локатор.

| Файл | testid |
|---|---|
| `Flow.Auth/Pages/Account/Login.cshtml` | `login-login`, `login-password`, `login-submit`, `login-error` |
| `Flow.Auth/Pages/Account/ChangePassword.cshtml` | `pwd-current`, `pwd-new`, `pwd-confirm`, `pwd-submit`, `pwd-error` |
| `Layout/Sidebar.razor` | `sidebar`, `sidebar-boards`, `sidebar-users`, `sidebar-me`, `sidebar-logout` |
| `Pages/Boards.razor` | `boards-page`, `boards-create`, `boards-list`, `boards-empty` |
| `Components/BoardCard.razor` | `board-card` (+ `data-board-key`), `board-card-name`, `board-card-count` |
| `Components/CreateBoardDialog.razor` | `board-dialog`, `board-dialog-name`, `board-dialog-key`, `board-dialog-submit`, `board-dialog-error` |
| `Pages/Board.razor` | `board-page`, `board-title`, `task-create`, `tasks-list`, `tasks-empty` |
| `Components/TaskRow.razor` | `task-row` (+ `data-task-code`), `task-row-title`, `task-row-status`, `task-row-assignee` |
| `Components/TaskDrawer.razor` | `task-drawer`, `task-drawer-title`, `task-drawer-description`, `task-drawer-save`, `task-drawer-close` |
| `Components/StatusSelect.razor` / `StatusFilter.razor` | `status-select`, `status-option` (+ `data-status-id`), `status-filter` |
| `Components/AssigneeSelect.razor` / `AssigneeFilter.razor` | `assignee-select`, `assignee-option` (+ `data-user-id`), `assignee-filter` |
| `Pages/UsersPage.razor` | `users-page`, `users-create`, `users-list`, `user-row` (+ `data-user-id`) |
| `Components/CreateUserDialog.razor` | `user-dialog`, поля `user-dialog-{username,email,first,last,password}`, `user-dialog-submit` |
| `Components/RoleSelect.razor` / `RoleChip.razor` | `role-select`, `role-option` (+ `data-role`), `role-chip` |
| `Components/UserMenu.razor` / `RowMenu.razor` | `user-menu`, `user-menu-deactivate`, `row-menu`, `row-menu-rename`, `row-menu-delete` |
| `Pages/UserPage.razor` | `profile-page`, `profile-save`, `profile-password-card`, `profile-password-submit` |
| `Components/Toast.razor` / `ConfirmDialog.razor` / `Popover.razor` | `toast`, `confirm-dialog`, `confirm-ok`, `confirm-cancel`, `popover` |

## Сценарии

P0 — обязательны в первом этапе, гейт для мержа. P1 — второй этап.

| # | Приоритет | Сценарий | Ожидание |
|---|---|---|---|
| A1 | P0 | Анонимный заход на `/boards` | редирект на страницу входа Flow.Auth |
| A2 | P0 | Вход bootstrap-Owner с начальным паролём | страница смены пароля, код не выдан |
| A3 | P0 | Смена начального пароля | вернулись в клиент авторизованными, в сайдбаре профиль Owner |
| A4 | P0 | Вход с неверным паролём | остались на форме, `login-error` виден |
| A5 | P0 | «Выйти» | ушли на `authentication/logged-out`, повторный заход на `/boards` снова просит вход (не залипаем в цикле редиректов) |
| A6 | P1 | Lockout: 5 неверных попыток | шестая отклонена даже с верным паролём |
| B1 | P0 | Создать проект | карточка в списке, `GET /boards` содержит проект с нормализованным `Key` |
| B2 | P0 | Дубликат ключа проекта | 409 → ошибка в диалоге, второй проект не создан |
| B3 | P0 | Переименовать проект | новое имя в карточке и после перезагрузки страницы |
| B4 | P0 | Удалить проект с задачами | карточка исчезла, `GET /boards/{id}` → 404 |
| B5 | P1 | Невалидный ключ (`ab`, `1AB`, 11 символов) | кнопка submit заблокирована / ошибка поля, запроса нет |
| C1 | P0 | Создать задачу | строка в списке, код вида `KEY-1`, статус — начальный |
| C2 | P0 | Открыть задачу в drawer, сменить название и описание | сохранилось, видно в строке и на `/tasks/{id}` |
| C3 | P0 | Сменить статус из строки | новый статус в строке и в `GET /tasks/{id}` |
| C4 | P0 | Назначить исполнителя через `@`-автодополнение | аватар в строке, `AssigneeId` в API |
| C5 | P0 | Снять исполнителя | строка без аватара, `AssigneeId = null` |
| C6 | P1 | Фильтр по исполнителю (`?who=<guid>`, `?who=none`) | список сузился, фильтр выживает перезагрузку |
| C7 | P1 | Нельзя назначить деактивированного | его нет в списке выбора |
| C8 | P1 | Удалить задачу | строка исчезла, `GET /tasks/{id}` → 404 |
| D1 | P0 | Owner добавляет человека с начальным паролём | появился в «Людях» со статусом «приглашён» |
| D2 | P0 | Новый человек входит своим паролём | обязательная смена пароля, после неё статус «активен» |
| D3 | P0 | Owner меняет роль человека | `role-chip` обновился, `GET /users/{id}` подтверждает |
| D4 | P1 | Owner деактивирует и активирует человека | статус в UI и в API, деактивированный не может войти |
| D5 | P1 | Дубликат username/email | 409 → ошибка в диалоге |
| D6 | P1 | Сброс пароля чужому и смена своего с текущим | вход новым паролём проходит |
| E1 | P0 | Reader | нет «Создать проект», нет «Создать задачу», селекты статуса/исполнителя — пилюли |
| E2 | P0 | Member на своей задаче | правит свою, назначает только себя; на чужой — только чтение |
| E3 | P0 | Developer | правит любую задачу, назначает любого; кнопок управления проектами нет |
| E4 | P0 | Admin | проекты и люди доступны; роли Admin/Owner в `role-select` выключены |
| E5 | P0 | 403 от сервера | тост с текстом ошибки, состояние UI не разъехалось |
| E6 | P1 | Деактивированный actor | 401 → редирект на вход |
| F1 | P1 | Поповер у нижней кромки окна | остаётся в viewport и привязан к якорю |
| F2 | P1 | Хоткеи: `N` — создать, `Esc` — закрыть, `↑`/`↓` — по списку | работают, фокус не теряется |
| F3 | P1 | `prefers-reduced-motion` | без transform-анимаций, функциональность та же |
| F4 | P1 | Перезагрузка на `/tasks/{id}` (deep link) | страница открывается авторизованной, без 404 |
| F5 | P1 | Flow.Api недоступен (остановить контейнер) | тост про 502, клиент не падает в белый экран |

## Стабильность (правила, а не пожелания)

- Только web-first ассерты `Expect(locator).ToBeVisibleAsync()` / `ToHaveTextAsync()`;
  `Thread.Sleep`, `WaitForTimeoutAsync`, `WaitForSelector` с ручным таймаутом — запрещены,
  кроме одного места: прогрев первой загрузки WASM.
- Локаторы — `Page.GetByTestId`, и они ленивые: Blazor перерисовывает DOM, найденный заранее
  `IElementHandle` отваливается с «element is not attached».
- **Первая загрузка клиента — это скачивание WASM**: `NavigationTimeout` 60 с,
  `ExpectTimeout` 10 с, остальные — дефолт. После прогрева в `StackFixture` браузерный кеш общий.
- Никаких ожиданий «по индексу в списке» — только по `data-board-key` / `data-task-code` /
  `data-user-id` созданной тестом сущности.
- Ретраев нет. Тест, упавший дважды подряд на одном и том же месте без изменения кода, получает
  трейт `[Trait("Category", "Quarantine")]`, исключается из джобы CI **и заводится баг** —
  карантин без задачи запрещён.
- Сидирование данных для сценария — по API (`FlowApiClient`), не через UI: UI-подготовка на
  каждый тест удваивает время и добавляет точки отказа. Через UI проходится только то, что тест проверяет.

## Артефакты отладки

- Трассировка: `Tracing.StartAsync(screenshots: true, snapshots: true, sources: true)` в
  `InitializeAsync`, `StopAsync` в `DisposeAsync` — файл сохраняется **только для упавшего теста**
  (`TestResults/e2e/traces/<класс>.<метод>.zip`).
- Видео и скриншот при падении — туда же. Локально смотреть `playwright show-trace <zip>`.
- Логи стека при падении: `docker compose -p flow-e2e logs --no-color > TestResults/e2e/stack.log`
  на шаге CI с `if: failure()`.
- Всё это грузится в артефакт workflow `e2e-results` (retention 7 дней).

## CI

Новый `.github/workflows/e2e.yml` (`workflow_call`), вызывается из `ci.yml` после `build`
и параллельно с docker-джобами. Шаги:

1. checkout, `setup-dotnet` по `global.json`, кеш NuGet;
2. `dotnet build tests/Flow.E2E.Tests -c Release`;
3. установка браузера: `pwsh tests/Flow.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`
   (кеш `~/.cache/ms-playwright` по хешу `Flow.E2E.Tests.csproj`);
4. `docker compose -p flow-e2e -f docker-compose.yml -f docker-compose.e2e.yml up -d --build --wait`;
5. `dotnet test tests/Flow.E2E.Tests -c Release --no-build --filter "Category!=Quarantine" --logger "trx;LogFileName=e2e.trx"`;
6. `if: failure()` — логи compose; `if: always()` — `upload-artifact` с trx, трейсами, видео, логами;
7. `if: always()` — `docker compose -p flow-e2e down -v`.

`timeout-minutes: 30`. Джоба блокирует мерж в `main` (как остальные) — но только по P0-сценариям:
P1 до полной стабилизации помечаются `Category=Extended` и гоняются на `workflow_dispatch`
и по расписанию, чтобы флак второго этапа не вставал на пути релизов.

`dotnet test Flow.slnx` в `build.yml` **не должен** захватывать E2E (стека там нет):
проект исключается из общего прогона — в `build.yml` тесты вызываются по путям, так что
достаточно не добавлять его в список шагов; отдельно проверить, что никто не запускает `dotnet test Flow.slnx` в CI.

## Локальный запуск

```bash
cp .env.example .env                 # порты e2e — дефолты в docker-compose.e2e.yml
docker compose -p flow-e2e -f docker-compose.yml -f docker-compose.e2e.yml up -d --build --wait
dotnet build tests/Flow.E2E.Tests
pwsh tests/Flow.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # один раз
dotnet test tests/Flow.E2E.Tests                                              # headless
E2E_HEADED=1 dotnet test tests/Flow.E2E.Tests --filter "FullyQualifiedName~TasksTests"
docker compose -p flow-e2e down -v
```

Ловушки локального прогона: занятые порты (правятся в `.env`), старый том с уже смененным
паролём Owner (`down -v` или `E2E_OWNER_PASSWORD` из прошлого прогона), не переустановленный
браузер после обновления пакета Playwright (версия браузера привязана к версии пакета).

## Этапы работ

| Этап | Состав | Результат |
|---|---|---|
| 1 | `docker-compose.e2e.yml`, healthcheck'и auth/api, каркас проекта, `StackFixture` + `FlowPageTest` + `FlowApiClient`, сценарии A1–A5 | вход и выход проверяются в CI |
| 2 | `data-testid` по таблице, POM всех экранов, B1–B4, C1–C5, D1–D3 | основные пути под тестами |
| 3 | E1–E5 (роли), `e2e.yml` как блокирующая джоба | права проверяются в браузере |
| 4 | P1-сценарии (A6, B5, C6–C8, D4–D6, E6, F1–F5) под `Category=Extended` | расширенный набор по расписанию |

## Чек-лист приёмки

- [ ] `tests/Flow.E2E.Tests` в `Flow.slnx`, собирается на .NET 10, ноль warning'ов.
- [ ] `docker-compose.e2e.yml` поднимает стек на портах 5434/9010/5101/8081/5017 и не конфликтует
      с запущенной локальной разработкой; `up --wait` возвращается только когда auth и api отвечают.
- [ ] Все P0-сценарии зелёные на чистых томах **и** на повторном прогоне без `down -v`.
- [ ] Ни одного `Thread.Sleep` / `WaitForTimeoutAsync` в тестах, кроме прогрева WASM.
- [ ] Селекторы — только `GetByTestId` и `data-*`-атрибуты; поиск по русскому тексту и по
      DRESSY-классам отсутствует.
- [ ] Тесты убирают созданные проекты и задачи; повторный прогон 10 раз подряд не растит базу.
- [ ] Трейс, видео и логи compose приложены к упавшему прогону и открываются `show-trace`.
- [ ] Джоба E2E в `ci.yml`, зелёная на PR, красная при намеренно внесённой регрессии
      (проверить: убрать `[Authorize]` c одной страницы → падает A1; сломать `Board.Key` валидацию → падает B5).
- [ ] `data-testid` добавлены без изменения поведения и вёрстки: остальные тесты и UI не поехали.
- [ ] AGENTS.md — раздел про E2E и ссылка на это ТЗ в «Навигации».

## Открытые вопросы

- **Прогон против staging.** Схема с env-адресами это позволяет, но bootstrap-Owner и создание
  людей на общем стенде — нет. Понадобится отдельный сервисный пользователь и запрет
  деструктивных сценариев (B4, D4) — решается, когда появится staging.
- **Визуальная регрессия.** `ToHaveScreenshotAsync` даёт дешёвый контроль тёмной темы и
  поповеров, но требует эталонов, привязанных к ОС и версии браузера. Отложено: сначала
  стабильный функциональный набор.
- **Параллелизм.** На старте — классы параллельно, внутри класса последовательно. Если прогон
  перевалит за 10 минут, режем шардингом по классам через несколько джоб `matrix`.
