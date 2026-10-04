# ТЗ: интеграция с Git-хостингами — GitHub, GitLab, Gitea, Forgejo

Статус: **этапы 5A–5E сделаны** (подключения по токену и GitHub App, репозитории, вебхуки, разбор push/PR/веток, блок «Разработка», дозагрузка истории, диагностика доставок, FQL `development`, автопереходы и смарт-коммиты, ветка и PR из карточки, комментарий в PR, PR и коммиты в поиске). Часть плана `docs/TZ_roadmap_jira_parity.md` (блок 5). Образец — Windshift
(`internal/scm/*`, `internal/database/schema/git_postgres.sql`).

## Исходное требование

Привязка коммитов, веток и pull/merge request'ов к задачам Flow для GitHub, GitLab, Gitea и Forgejo.

## Принятые решения

- Код, контракты и компоненты используют имена `Git*`, а папки и namespaces — `GitIntegration`. SQL-таблицы — `GitHostConnections`, `GitRepositories`, `GitRepositoryBoards`, `GitDevelopmentLinks`, `GitIntegrationJobs`; миграция `RenameScmTablesToGit` переименовывает существующие таблицы `Scm*` с сохранением данных. Маршруты области — `/api/git` и `/hooks/git`. Для совместимости сохранены исторические миграции, секция настроек `Scm`, переменная `SCM_PUBLIC_BASE_URL` и DataProtection purpose `Flow.Scm`.

- **Основной канал — вебхуки хостинга, а не опрос API.** Хостинг сам сообщает о push и PR. API нужен для
  регистрации вебхука, дозагрузки истории при подключении и (этап 5D) действий из Flow.
- **Задача находится по коду в тексте**: имя ветки, заголовок и описание PR, сообщение коммита. Регулярка ключа
  та же, что у `Board.Key` (`\b([A-Z][A-Z0-9]{1,9})-(\d+)\b`), плюс алиасы кодов после переноса задачи
  (`TaskCodeAliases`, `docs/TZ_task_model.md` §6).
- **Репозиторий явно привязывается к проектам.** Код задачи связывается, только если её проект привязан
  к репозиторию-источнику. Иначе публичный репозиторий с коммитом «fix WEB-12» прицепил бы чужой текст к
  задаче приватного проекта, а совпадения ключей между организациями дали бы мусорные связи.
- **Forgejo = Gitea по протоколу.** Forgejo — форк Gitea с совместимым API и вебхуками. Один адаптер
  `GiteaProvider`, различие — только в заголовке подписи (`X-Forgejo-Signature` или `X-Gitea-Signature`)
  и в подписи в интерфейсе.
- **Обработка асинхронная, через очередь в БД** — тот же приём, что у `SearchIndexQueue`. Эндпоинт вебхука
  проверяет подпись, пишет доставку и сразу отвечает 202. Хостинг ждёт ответ ~10 секунд и считает таймаут
  ошибкой, а разбор push на сотню коммитов с поиском задач в это окно может не уложиться.
- **Коммиты не пишутся в журнал задачи.** Сотня коммитов превратила бы ленту в лог git. Для них — отдельный
  блок «Разработка» в карточке. В журнал попадает только то, что меняет задачу (автоматический переход статуса).
- **Действия от имени бота — отдельный профиль.** `TaskActivity.ActorId` ссылается на `Users` (FK Restrict).
  Автопереход без известного автора пишется от профиля `flow-bot` (сеется как bootstrap-пользователь, без учётной
  записи в Auth-модуле: войти им нельзя).

## 1. Модель

```
GitHostConnection: Id, Provider : GitHub|GitLab|Gitea|Forgejo, Name, BaseUrl (для self-hosted; GitHub.com — null),
  AuthKind : Token|GitHubApp, SecretProtected (DataProtection), AppId?, InstallationId?,
  CreatedById, CreatedAt, LastCheckAt?, LastError?
GitRepository: Id, ConnectionId, ExternalId, FullName (org/repo), WebUrl, DefaultBranch,
  WebhookId?, WebhookSecretProtected, IsActive, LastDeliveryAt?,
  SyncState : Pending|Syncing|Ready|Failed, LastSyncedCommit?, LastSyncedAt?, LastSyncError?
GitRepositoryBoard: (RepositoryId, BoardId) PK, CreatedById, CreatedAt,
  OnPullRequestOpenedStatusId?, OnPullRequestMergedStatusId?, SmartCommits, CommentOnPullRequests
GitDevelopmentLink: Id, TaskId (FK cascade), RepositoryId (FK cascade), Kind : Branch|Commit|PullRequest,
  ExternalId (sha / номер PR / имя ветки), Url, Title, State : Open|Draft|Merged|Closed|null,
  AuthorLogin, AuthorUserId?, SourceBranch?, TargetBranch?, OccurredAt, UpdatedAt
  unique (TaskId, RepositoryId, Kind, ExternalId)
GitIntegrationJob: Id, RepositoryId, DeliveryId (из заголовка), Event, ReceivedAt, Status : Pending|Done|Failed|Ignored,
  Attempts, NextAttemptAt, LastError, Payload : jsonb (обрезанный до нужных полей)
  unique (RepositoryId, DeliveryId)
```

- `Board.RepositoryBindings` показывает связи с репозиториями. Один `GitRepository` может быть привязан к нескольким проектам; локальная копия хранится по Id репозитория и не удаляется при удалении одного Board.
- Локальная синхронизация пока работает с публичными HTTPS-репозиториями github.com и gitlab.com; `IsActive` относится к вебхукам и не заменяет `SyncState`.
- Секреты (токены, ключ GitHub App, секрет вебхука) шифруются `IDataProtector` с purpose `Flow.Scm`. Ключи
  DataProtection уже хранятся на диске (`DataProtection:KeysPath`, том `auth-keys`). Если путь не задан, ключи
  эфемерные и после рестарта секреты не расшифруются. Тогда подключение помечается «нужно переподключить»
  с понятной ошибкой, а на старте в лог пишется предупреждение.
- Обрезанный `Payload` хранит только поля, которые разбирает Flow (не весь JSON вебхука): push хостинга может
  весить мегабайты. Доставки старше 30 дней удаляет тот же воркер.

## 2. Приём вебхуков

- После перехода на маршруты `Git` ранее зарегистрированные вебхуки нужно вручную перенастроить на
  `{PublicBaseUrl}/hooks/git/{repositoryId}`. Старый адрес больше не обслуживается; настройки на внешних
  Git-хостингах автоматически не изменяются.
- Маршрут `POST /hooks/git/{repositoryId}` — **вне `/api`** и с `[AllowAnonymous]`: вебхук не несёт Bearer,
  а под `/api` принимается только Bearer. `FallbackPolicy` закрывает всё прочее, поэтому анонимность здесь
  явная и единственная, как у `/health/*`.
- `RequestSizeLimit` 5 МБ; больше — 413. GitHub режет payload на 25 МБ, но push с таким телом Flow всё равно
  не нужен.
- Проверка подписи (до разбора JSON, по сырому телу, сравнение за постоянное время —
  `CryptographicOperations.FixedTimeEquals`):

  | Провайдер | Заголовок | Алгоритм |
  |---|---|---|
  | GitHub | `X-Hub-Signature-256: sha256=<hex>` | HMAC-SHA256(секрет, тело) |
  | GitLab | `X-Gitlab-Token: <секрет>` | сравнение секрета |
  | Gitea | `X-Gitea-Signature: <hex>` | HMAC-SHA256 |
  | Forgejo | `X-Forgejo-Signature: <hex>` (или `X-Gitea-Signature`) | HMAC-SHA256 |

  Неверная подпись → 401 без тела и запись в лог. Неизвестный или выключенный репозиторий → 404.
- Идентификатор доставки: `X-GitHub-Delivery`, `X-Gitlab-Event-UUID`, `X-Gitea-Delivery` / `X-Forgejo-Delivery`.
  Повтор с тем же Id → 200 без повторной обработки (хостинги повторяют доставку при таймауте).
- Принимаемые события: push, pull_request / merge_request, create/delete (ветки). Остальные → 202 + `Ignored`,
  чтобы вебхук с лишними галочками не копил ошибки.

## 3. Разбор событий (`GitWorker : BackgroundService`)

Цикл как у `SearchIndexingRunner`: `FOR UPDATE SKIP LOCKED`, savepoint на доставку, backoff 5 с → 5 мин,
`MaxAttempts` = 8.

| Событие | Где ищем коды | Что делаем |
|---|---|---|
| Ветка создана | имя ветки | `GitDevelopmentLink(Branch)` |
| Ветка удалена | — | у связи `State = Closed`; связь остаётся (в истории видно, что работа была) |
| Push | сообщения коммитов (до 100 на push, остальное — ссылкой «ещё N») и имя ветки | `GitDevelopmentLink(Commit)` на каждый коммит с кодом; коммиты без кода, но в ветке с кодом, связываются с задачей ветки |
| PR открыт / изменён | заголовок, описание, исходная ветка | `GitDevelopmentLink(PullRequest)`, `State` |
| PR смёржен / закрыт / переоткрыт | те же | обновить `State`; автопереход (§4) |

- Коды ищутся только в проектах, привязанных к репозиторию (`GitRepositoryBoard`). Код, не найденный в этих
  проектах, игнорируется молча.
- `AuthorUserId` — сопоставление по e-mail коммита с `Users.Email` или по логину с `UserLink` типа `GitHub`/`GitLab`
  (`UserLinkType` уже есть; для Gitea/Forgejo добавить `Gitea = 6` в конец enum). Не нашли — показываем логин
  без аватара.
- Force-push, который переписал коммиты: старые `GitDevelopmentLink(Commit)` не удаляются. Сопоставлять историю ради их
  удаления не стоит — коммит по ссылке на хостинге всё равно откроется или покажет 404.
- Заголовки PR и сообщения коммитов — **недоверенный текст**: показываются как текст, без Markdown и без HTML,
  в индекс поиска попадают с этапа 5E — тоже как текст.

## 4. Автопереходы статусов

- Настройка на связку «репозиторий × проект» (`GitRepositoryBoard.OnPullRequestOpenedStatusId и OnPullRequestMergedStatusId`), по умолчанию выключены:
  «PR открыт → статус X», «PR смёржен в ветку по умолчанию → статус Y».
- Переход проходит **через проверку workflow** (`docs/TZ_workflow_config.md` §2). Запрещённый переход не
  выполняется, в блоке «Разработка» появляется пометка «автопереход не разрешён workflow». Обхода нет.
- Actor — сопоставленный автор PR, если у него есть право `EditTask` на задачу, иначе `flow-bot`. Журнал:
  `StatusChanged` с пометкой источника (`TaskActivity.Source = Git` — новое необязательное поле; клиент
  показывает «по PR #42»).
- Задача уже в финальном статусе — merge её не трогает; переоткрытый PR назад не переводит.

## 5. Смарт-коммиты (этап 5C)

- Синтаксис в сообщении коммита: `WEB-12 #done`, `WEB-12 #comment текст`, `WEB-12 #status "В работе"`,
  `WEB-12 #time 2h` — вне объёма (worklog нет).
- Выполняются, только если автор коммита сопоставлен с активным пользователем Flow и у него есть права на это
  действие в проекте (`EnsureCanEditTask`, `EnsureCanComment`). Бот смарт-коммиты не выполняет: иначе любой
  с правом push в привязанный репозиторий закрывал бы задачи.
- Выполняются только для коммитов, попавших в ветку по умолчанию (push в неё). Смарт-коммиты в feature-ветках
  срабатывали бы при каждом rebase.
- Включаются флагом на связке «репозиторий × проект».

## 6. Подключение и настройка

- Экран «Интеграции» (`/settings/integrations`, глобальный Admin+): подключение — провайдер, адрес (для
  self-hosted), токен или GitHub App. Кнопка «Проверить» дёргает `GET /user` / `GET /api/v4/user` /
  `GET /api/v1/user`.
- Минимальные права токена: чтение репозитория и управление вебхуками (GitHub — `repo` или fine-grained
  `Metadata:read`, `Contents:read`, `Pull requests:read`, `Webhooks:write`; GitLab — `api` на уровне проекта
  или группы; Gitea/Forgejo — `read:repository`, `write:repository` для хуков). В интерфейсе — подсказка со
  ссылкой на страницу создания токена у провайдера.
- Выбор репозиториев — список из API подключения с поиском. При добавлении Flow сам создаёт вебхук со случайным
  32-байтным секретом и адресом `{PublicBaseUrl}/hooks/git/{id}`. `PublicBaseUrl` — новая настройка
  (`Scm:PublicBaseUrl`): внутри контейнера хост не знает, как его видят снаружи. Если вебхук создать не удалось
  (нет прав), экран показывает адрес и секрет для ручной настройки.
- Привязка репозитория к проекту — в настройках проекта, вкладка «Разработка» (право `ManageGit`,
  `docs/TZ_project_access.md`). Один репозиторий можно привязать к нескольким проектам (монорепозиторий).
- Дозагрузка истории при привязке (этап 5B): последние 100 PR и 30 дней коммитов ветки по умолчанию через API,
  фоновой задачей, с учётом rate limit (`X-RateLimit-Remaining` / `RateLimit-Remaining`; при исчерпании — пауза
  до сброса).
- Отключение репозитория удаляет вебхук на хостинге (best-effort) и выключает приём. `GitDevelopmentLink` остаются.

## 7. Клиент

- Блок «Разработка» в `TaskPage` и `TaskDrawer`: ветки, PR (номер, заголовок, статус чипом Open/Draft/Merged/
  Closed, автор, целевая ветка), коммиты (последние 5 + «ещё N»). Ссылки открываются на хостинге в новой вкладке.
- Кнопка «Создать ветку» — без обращения к API: копирует `git checkout -b WEB-12-kratkoe-nazvanie`
  (транслит из `Services/Ru.cs`, как для username).
- В строке списка и на карточке канбана — значок PR с состоянием (последний PR задачи).
- FQL (`docs/TZ_task_views.md` §7): `development = openPR | mergedPR | noPR`.

## 8. Этап 5D: действия из Flow

Создание ветки и PR через API из карточки задачи, комментарий в PR со ссылкой на задачу. Это запись в чужую систему:
нужны права токена на запись и своё право проекта «кто в Flow может писать в репозиторий» (`WriteGit`).

## Как сделано (этап 5A)

- Домен — `Domain/GitIntegration/`: `GitHostConnection` (токен только зашифрованным, результат проверки), `GitRepository`
  (`Reactivate` при повторном подключении — связи задач не теряются), `GitRepositoryBoard`, `GitDevelopmentLink` (unique по задаче,
  репозиторию, виду и внешнему id), `GitIntegrationJob` (backoff 5 с → 5 мин, 8 попыток). `ProjectPermission.ManageGit`
  (администратор проекта), `UserLinkType.Gitea = 6`. Миграция `AddScmIntegration`.
- Чистые функции Application (`Features/GitIntegration`): `TaskCodeDetector` (коды на границах слова; в имени ветки — без учёта
  регистра, `web-12-login` тоже WEB-12), `GitSignatures` (HMAC-SHA256 и токен GitLab, `FixedTimeEquals`),
  `GitPayloadParser` → `GitEvent`. Нормализованное событие и есть `Payload` доставки: в очереди лежат только поля Flow.
  У GitLab создание и удаление ветки — тот же Push Hook с нулевым before/after.
- Приём — `GitWebhookController` (`POST /hooks/git/{id}`, вне `/api`, `[AllowAnonymous]`, 5 МБ): подпись → повтор
  (Id доставки; у старого GitLab без UUID — SHA-256 тела) → нормализация → очередь → 202. Разбор — `GitWorker`
  (Infrastructure, scope на доставку, сбой — `GitIntegrationJobFailCommand`, чистка доставок старше 30 дней). Вместо
  `FOR UPDATE SKIP LOCKED` — идемпотентность: связь уникальна и обновляется, повтор даёт то же состояние.
- Клиенты хостингов — `Infrastructure/GitIntegration/GitProviderClient` (GitHub, GitLab, Gitea/Forgejo): проверка, список, репозиторий по id,
  создание и удаление вебхука; тело запроса — с Content-Length, ошибки — понятным текстом. Шифрование —
  `Flow.Api/GitIntegration/DataProtectionGitSecretProtector` (purpose «Flow.Scm»); не расшифровалось — «нужно ввести токен заново».
- Автор — по e-mail коммита, иначе по логину: последний сегмент ссылки профиля нужного хостинга (GitHub/GitLab/Gitea).
- Отступления: FQL `development` и дозагрузка истории — этап 5B; `AutoTransitions` у привязки не заведены — этап 5C;
  без `Scm:PublicBaseUrl` вебхук не создаётся, и экран показывает адрес и секрет для ручной настройки.
- Клиент: «Настройки → Интеграции» (`Components/GitIntegrations`, `GitHostConnectionDialog`, `GitRepositoryDialog`), значок
  «Репозитории проекта» в топбаре «Задач» (`BoardRepositoriesDialog`, ManageGit), блок «Разработка» в карточке и
  слайдере (`Components/TaskDevelopment`: PR с состоянием, ветки, последние 5 коммитов, «Ветка» копирует
  `git checkout -b`), значок PR в строке списка и на карточке канбана (`TaskResponse.PullRequestState`).

## Как сделано (этап 5B)

- **Дозагрузка истории** живёт в той же очереди, что вебхуки: задание — `GitIntegrationJob` с событием `flow:backfill`
  (`GitIntegrationJob.CreateBackfill`). Отдельной таблицы и воркера нет — повторы, backoff, диагностика и срок хранения общие.
  Ставится при каждой новой привязке репозитория к проекту (задачи проекта уже упоминались в PR до привязки) и вручную —
  `POST /git/repositories/{id}/backfill`; второе задание поверх стоящего в очереди не ставится (`HasPendingDeliveryAsync`).
  Разбор: `IGitProviderClient.GetHistoryAsync` → PR по возрастанию даты изменения (последнее состояние пишется
  последним) и коммиты ветки по умолчанию одним push-событием — тем же `Processor`, что вебхуки. Объём —
  `Scm:BackfillPullRequests` (100), `BackfillCommitDays` (30, от момента постановки), `BackfillMaxCommits` (1000).
- Постранично: GitHub и GitLab — по 100, Gitea/Forgejo — по 50 (их `MAX_RESPONSE_ITEMS`, иначе короткая страница ложно
  значила бы конец). Старые Gitea параметр `since` не знают — клиент останавливается на первом коммите старше даты сам.
- **Лимит запросов**: 429 всегда, 403 — если `X-RateLimit-Remaining`/`RateLimit-Remaining` = 0 или есть `Retry-After`
  (вторичный лимит GitHub). Клиент бросает `GitRateLimitException(ResetAt)` — `Retry-After`, иначе `*-RateLimit-Reset`
  (unix-время), иначе минута. Задание откладывается `GitIntegrationJob.Postpone` **без траты попытки** — это пауза, а не сбой.
  Обычный 403 без нулевого остатка — по-прежнему «не хватает прав».
- **GitHub App**: `GitHostConnection.AuthKind` (Token | GitHubApp), `AppId`, `InstallationId` (миграция `AddScmGitHubApp`),
  в `SecretProtected` — закрытый ключ PEM (переводы строк сохраняются). Клиент подписывает JWT RS256
  (`GitHubAppJwt`: iat на минуту в прошлом, exp через 9 минут, iss — App ID), меняет его на токен установки
  (`POST /app/installations/{id}/access_tokens`) и держит в singleton-кэше `GitHubAppTokens` до истечения минус 5 минут;
  ключ кэша включает хеш PEM и оба Id. Проверка — токен установки плюс `GET /app` (логин `slug[bot]`), список
  репозиториев — `GET /installation/repositories`. Вебхуки — по-прежнему на репозиторий (право приложения Webhooks: write),
  а не общий вебхук приложения: секрет остаётся свой у каждого репозитория. Способ входа после создания не меняется.
- **Диагностика доставок**: `GitRepositoryResponse.FailedDeliveries` (один GROUP BY), `GitIntegrationJobResponse.NextAttemptAt`
  (у Pending — когда воркер возьмёт снова) и `IsBackfill`; повтор — `POST /git/deliveries/{id}/retry` (`GitIntegrationJob.Retry`:
  только из Failed, попытки с нуля). Клиент: в «Настройки → Интеграции» у репозитория ссылка «доставки» (или «N ошибок»
  красным) → `Components/GitIntegrationJobsDialog` — фильтр по состоянию, текст ошибки, «Повторить», «Дозагрузить историю».
- **FQL `development`**: `openPR` (есть PR в состоянии Open или Draft), `mergedPR`, `noPR` (ни одной связи вида
  PullRequest) — узел `TaskFilterPullRequest`, в SQL — `EXISTS` по `GitDevelopmentLinks`; `!=`, `IN`, `NOT IN` как у прочих
  перечислений. Подсказки значений — из `FqlFields.Development`.

## Как сделано (этап 5C)

- Настройки — колонками у привязки, а не jsonb: `GitRepositoryBoard.OnPullRequestOpenedStatusId`,
  `OnPullRequestMergedStatusId` (FK на `Statuses`, SetNull — удалили статус, автопереход выключился) и `SmartCommits`
  (миграция `AddScmAutomation`). `PUT /boards/{id}/repositories/{repoId}` принимает `{ onPullRequestOpenedStatusId,
  onPullRequestMergedStatusId, smartCommits }`, пустое тело — только привязать; статус чужого проекта — 400.
- Всё в `Features/GitIntegration/GitAutomation` — тот же путь, что правка человеком: `TransitionGuard.CheckAsync` (обхода нет),
  `TaskActivity.StatusChanged`, `Upsert` в поиске. Отказ не роняет доставку: пишется `GitDevelopmentLink.Note`
  («Автопереход в «X» не разрешён workflow: …», «Смарт-коммит не выполнен: …»), блок «Разработка» показывает её под PR
  или коммитом; успешный переход пометку снимает.
- Автопереход «PR открыт» — на первом появлении PR в состоянии Open или при выходе из черновика; «влит» — при переходе
  в Merged и только если целевая ветка = ветка по умолчанию. Задача в финальном статусе не трогается, переоткрытие назад
  не переводит. Actor — автор PR (логин из ссылок профиля), если он активен и может править задачу, иначе `flow-bot`:
  `GitIntegrationBot` — профиль с фиксированным Id, деактивированный и без учётной записи, создаётся при первой надобности в той же
  транзакции (FK журнала). Роль бота в проверке workflow — участник проекта (Member): переход с `MinRole` выше он не
  сделает.
- Смарт-коммиты — `SmartCommitParser`: построчно, команды строки относятся к кодам до первой команды, аргумент — до
  следующей команды; коды внутри аргумента — не цели. Выполняются только при `SmartCommits` у привязки, только в push'е
  в ветку по умолчанию, только от автора, сопоставленного по e-mail/логину с активным пользователем, и только с его
  правами (`EnsureCanComment`, `EnsureCanEditTask`, workflow). `#done` — первый финальный статус проекта, `#status` — по
  имени без учёта регистра. Повторно не выполняются: коммит, уже виденный в ветке по умолчанию, пропускается.
- Журнал: `TaskActivity.Source`/`SourceUrl` («PR #42», «коммит a1b2c3d» и ссылка на хостинг), ставится один раз
  (`FromSource`). Клиент пишет «· по PR #42» ссылкой, бот — «Flow Bot» даже до перезагрузки справочника людей.
- Дозагрузка истории (5B) автоматизацию не запускает: прошлое не двигает задачи сегодня.

## Как сделано (этап 5D)

- Право `ProjectPermission.WriteGit` (17) — в наборе Developer и выше (`EnsureCanWriteGit`); у своих наборов прав
  (4E) включается галочкой «Создавать ветки и PR из Flow». Писать можно только в активный репозиторий, привязанный к
  проекту задачи, иначе 400. Пишет токен подключения: у GitHub App — токен установки, у PAT — его владелец.
- `Features/GitIntegration/GitTaskActions`: `TaskGitBranchCreateCommand` (имя проверяет `GitUrls.ValidateBranch` — латиница,
  цифры, `._-/`, без `..`, `//`, `.lock`; от ветки по умолчанию, если исходная не задана) и
  `TaskGitPullRequestCreateCommand` (в ветку по умолчанию, заголовок «КОД Название», описание «Задача Flow: [КОД
  Название](адрес)», черновик — `draft` у GitHub, «Draft:» у GitLab, «WIP:» у Gitea/Forgejo). Связь пишется сразу, не
  дожидаясь вебхука (он её лишь обновит), автор — actor; новый PR проходит автопереход «PR открыт» (5C) от имени
  создавшего. Отказ хостинга (`GitProviderException`: ветка уже есть, токен без записи) — 400 с его текстом. Ответ —
  блок «Разработка» целиком (`TaskDevelopmentQuery`).
- Провайдеры (`IGitProviderClient`): GitHub — sha головы исходной ветки и `POST git/refs`, `POST pulls`, комментарий
  через `issues/{n}/comments`; GitLab — `POST repository/branches?branch=&ref=`, `merge_requests`, `merge_requests/{n}/notes`;
  Gitea/Forgejo — `POST branches {new_branch_name, old_branch_name}`, `pulls`, `issues/{n}/comments`.
- Комментарий в PR — флаг привязки `GitRepositoryBoard.CommentOnPullRequests` (миграция `AddScmPullRequestComments`,
  по умолчанию выключен, настраивается в «Репозитории проекта»). Ставится после разбора доставки на каждую **новую**
  связь PR (не при дозагрузке истории, не на закрытый PR), кроме PR, в описании которого адрес задачи уже есть (так
  PR, созданный из Flow, не получает дубль). Адрес задачи — `{Scm:PublicBaseUrl}/tasks/{КОД}`. Отказ хостинга не
  роняет доставку — пометка у связи «Комментарий в PR не оставлен: …».
- `TaskDevelopmentResponse.Repositories` (привязанные активные репозитории) и `CanWrite` — клиент показывает «Создать
  ветку» и «Создать PR» только с правом; диалог `Components/GitCreateDialog` (репозиторий — `MudSelect`, если их
  несколько; имя ветки — `Ru.BranchName`, исходная для PR — открытая ветка задачи; «Черновик» — `MudCheckBox`).

## Как сделано (этап 5E)

- Новый вид источника поиска `SearchSourceType.Development` (6): источник — строка `GitDevelopmentLinks` вида PullRequest или
  Commit. Ветки не индексируются: их имя — код и транслит названия задачи, которые и так в индексе.
- Текст чанка (`SearchSourceReader.ReadDevelopmentAsync`): PR — «PR #42 Заголовок» и строкой ниже «ветка → цель»;
  коммит — первая строка сообщения (больше Flow не хранит) и «коммит a1b2c3d». Шапка для эмбеддера —
  `ChunkHeaderBuilder.ForDevelopment`: «PROJ-142 · acme/web · PR #42». Проект — проект задачи (фильтр видимости
  приватных проектов работает как у комментариев), закрытость — от задачи, как у вложений.
- Очередь (`Features/GitIntegration/GitSearch`): разбор доставки ставит `Upsert` на новую связь и на изменившийся текст
  (заголовок, ветки), повтор того же события индекс не трогает; дозагрузка истории — приоритетом 1, позади правок
  людей. PR, созданный из Flow (5D), индексируется сразу. Удаление задачи ставит `Delete` её связям (каскад БД прошёл
  бы мимо очереди), перенос в другой проект — `Upsert` с новым проектом; удаление проекта чистит чанки по `BoardId`.
- Связи, накопленные до 5E, ставит в очередь миграция `IndexScmLinksInSearch` (фоновым приоритетом); `POST
  /search/reindex` с `types: ["development"]` делает то же вручную.
- Выдача: заголовок — заголовок PR или строка коммита, `TaskCode` и `ParentId` — задача связи, клик открывает задачу
  (там «Разработка» со ссылкой на хостинг). Клиент: вкладка «Разработка» на странице поиска, группа в подсказках
  строки поиска, счётчик «PR и коммиты» в «Настройки → Поиск»; текст выводится экранированным, как и всё в выдаче.

## API

| Метод | Путь | Кто |
|---|---|---|
| GET/POST | `/git/connections`; PATCH/DELETE `/git/connections/{id}`; POST `/git/connections/{id}/check` | Admin+ |
| GET | `/git/connections/{id}/available-repositories?q=` | Admin+ |
| POST/DELETE | `/git/repositories` `{ connectionId, externalId }`, `/git/repositories/{id}` | Admin+ |
| PUT/DELETE | `/boards/{id}/repositories/{repoId}` `{ onPullRequestOpenedStatusId?, onPullRequestMergedStatusId?, smartCommits, commentOnPullRequests }` | `ManageGit` |
| GET | `/tasks/{id}/development` — `GitDevelopmentLink` задачи, сгруппированные по типу | `ViewProject` |
| POST | `/tasks/{id}/development/branch` `{ repositoryId, name, fromBranch? }`, `/tasks/{id}/development/pull-request` `{ repositoryId, sourceBranch, targetBranch?, title?, draft }` — ответ «Разработка»; 400 — имя, непривязанный репозиторий, отказ хостинга | `WriteGit` |
| GET | `/git/repositories/{id}/deliveries?status=` — диагностика доставок | Admin+ |
| POST | `/git/deliveries/{id}/retry` — повторить доставку с ошибкой (не Failed — 400) | Admin+ |
| POST | `/git/repositories/{id}/backfill` — дозагрузить историю, 202 | Admin+ |
| POST | `/hooks/git/{repositoryId}` — приём вебхука | аноним + подпись |

## Тесты

- Application: детектор кодов (границы слова, `WEB-12a`, коды в URL, алиасы после переноса, только привязанные
  проекты), сопоставление авторов, автопереходы через workflow (разрешён / запрещён / задача уже закрыта),
  смарт-коммиты (права, только ветка по умолчанию, бот не выполняет).
- Infrastructure: очередь доставок (`SKIP LOCKED`, повтор, backoff), идемпотентность `GitDevelopmentLink` и
  `(RepositoryId, DeliveryId)`, шифрование секретов на DataProtection.
- Api: подпись каждого провайдера — фикстуры с настоящими payload'ами и заголовками из документации
  (верная подпись → 202, неверная → 401, повтор доставки → 200, 413 на большом теле); маршрут анонимен
  и **не** под `/api`.
- Провайдерские клиенты (`GitHubProvider`, `GitLabProvider`, `GiteaProvider`) — на записанных ответах
  (`HttpMessageHandler`-заглушка). Живые хостинги в CI не дёргаются.

## Этапы

| Этап | Состав | Зависит от |
|---|---|---|
| 5A | Подключения (токен), репозитории, вебхуки, разбор push/PR/веток, блок «Разработка» | `TZ_project_access` 4A (право `ManageGit`) — или Admin+ до него |
| 5B | Дозагрузка истории, GitHub App, диагностика доставок | 5A |
| 5C | Автопереходы и смарт-коммиты | 5A, `TZ_workflow_config` 3B |
| 5D | Действия из Flow (ветка/PR через API) | 5A |
| 5E | PR и коммиты в индексе поиска | 5A |

## Вне объёма

Bitbucket и Azure DevOps, синхронизация issues хостинга с задачами Flow (двусторонняя), CI-статусы сборок в
карточке, раннеры и coding-агенты (как в Windshift), `git receive-pack` и хостинг репозиториев.
