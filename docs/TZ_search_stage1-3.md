# Рабочее задание: векторный поиск, этапы 1–3 (схема, эмбеддер, индексация)

Ветка: `feat/search-index-foundation`. Базовое ТЗ — [`TZ_search_vector.md`](TZ_search_vector.md); здесь только то,
что делается в этой ветке, с точными именами файлов и критериями готовности.

**Границы ветки.** Индекс наполняется и обновляется, но **искать ещё нечем**: `GET /search` не делается (этап 4),
клиент не трогается (этап 5), умные фильтры и «похожие задачи» не делаются (этап 6), вложения и изображения
не делаются (этапы 7–8, зависят от ТЗ вложений). Всё, что появляется, закрыто флагом `Search:Enabled=false` —
поведение существующего API и клиента не меняется ни в одном сценарии.

## Согласованные решения (не пересматривать в рамках ветки)

| Решение | Значение |
|---|---|
| Модель | `Qwen3-Embedding-0.6B`, размерность **512** (MRL с перенормализацией) |
| Где крутится | **Сайдкар** `llama-server` по OpenAI-совместимому `POST /v1/embeddings` (`Provider=Http`) |
| Хранилище векторов | pgvector в той же базе, тип `halfvec(512)`, HNSW `halfvec_cosine_ops` |
| Партиционирование | **Нет.** Одна таблица; партиции — отдельная миграция, когда объём потребует |
| Воркер | `BackgroundService` внутри Flow.Api. Отдельного `Flow.Worker` в этой ветке нет |
| Источники | Задачи, комментарии, **проекты и люди** — все четыре типа |
| Архив | Индексируются и открытые, и закрытые задачи; **различаются флагом `IsClosed`** в чанке, а режим выдачи выбирается параметром запроса на этапе 4 (дефолт — открытые, переключатель — всё) |
| Провайдер `Onnx` | В этой ветке **не реализуется**, но абстракция и точка выбора закладываются |

## Этап 1. Схема и конфигурация

### 1.1 Образ Postgres

`postgres:16-alpine` → `pgvector/pgvector:pg16` в пяти местах (данные и тома не меняются, расширение просто есть в образе):

- `docker-compose.data.yml` — сервисы `postgres` и `backup`;
- `tests/Flow.Infrastructure.Tests/PostgresFixture.cs:25`;
- `tests/Flow.Infrastructure.Tests/RoleMigrationTests.cs:20`;
- `tests/Flow.Api.Tests/ApiFixture.cs:30`;
- `tests/Flow.Auth.Tests/AuthFixture.cs:23` (для однородности; расширение там не нужно).

### 1.2 Пакеты и включение pgvector

`src/Flow.Infrastructure/Flow.Infrastructure.csproj`: `Pgvector`, `Pgvector.EntityFrameworkCore`.

В `FlowInfrastructureServiceCollectionExtensions.AddFlowInfrastructure`:

```csharp
services.AddDbContext<FlowDbContext>(options => options
    .UseNpgsql(connectionString, o => o.UseVector()));
```

### 1.3 Сущности индекса

Новый каталог `src/Flow.Infrastructure/Search/Entities` — **`internal`, EF-only, в `Flow.Domain` не выносить**:
вектор — деталь хранилища, доменных инвариантов у этих записей нет.

`SearchChunk`:

| Свойство | Тип C# | Столбец | Замечания |
|---|---|---|---|
| `Id` | `Guid` | uuid PK | |
| `SourceType` | `SearchSourceType` | int | `Task = 1, Comment = 2, Board = 3, User = 4, Attachment = 5` — `Attachment` завести сразу, не использовать |
| `SourceId` | `Guid` | uuid | |
| `BoardId` | `Guid?` | uuid null | Для задач и комментариев — доска; для `Board` — он сам; для `User` — null |
| `ChunkIndex` | `int` | int | 0 для коротких источников |
| `Content` | `string` | text | Чистый текст без шапки |
| `ContentHash` | `byte[]` | bytea | SHA-256 от `Content` |
| `Embedding` | `Pgvector.HalfVector` | `halfvec(512)` | |
| `Tsv` | — | tsvector GENERATED STORED | `to_tsvector('russian', "Content")`; в модели — `builder.Property<NpgsqlTsVector>("Tsv")` с `HasComputedColumnSql(..., stored: true)` |
| `IsClosed` | `bool` | boolean | Задача в финальном статусе; для остальных типов `false`. Обновляется `UPDATE` без реэмбеддинга |
| `ModelVersion` | `string` | text | |
| `SourceUpdatedAt` | `DateTime` | timestamptz | |
| `IndexedAt` | `DateTime` | timestamptz | |

Индексы: HNSW по `Embedding` (`halfvec_cosine_ops`, `m=16`, `ef_construction=64`), GIN по `Tsv`,
btree `(SourceType, SourceId)`, btree `(BoardId, IsClosed)`, btree `ContentHash`,
unique `(SourceType, SourceId, ChunkIndex, ModelVersion)`.

`SearchIndexRequest` (таблица `SearchIndexQueue`):

| Свойство | Тип | Замечания |
|---|---|---|
| `Id` | `Guid` | PK |
| `SourceType` | `SearchSourceType` | |
| `SourceId` | `Guid` | |
| `BoardId` | `Guid?` | |
| `Operation` | `SearchIndexOperation` | `Upsert = 1, Delete = 2` |
| `Priority` | `int` | 0 — живые правки, 1 — массовая переиндексация |
| `EnqueuedAt` | `DateTime` | timestamptz |
| `AttemptCount` | `int` | |
| `NextAttemptAt` | `DateTime` | timestamptz |
| `LastError` | `string?` | text null |

Индексы: btree `(Priority, NextAttemptAt)`, unique `(SourceType, SourceId, Operation)`.

Конфигурации — `src/Flow.Infrastructure/Persistence/Configurations/SearchChunkConfiguration.cs` и
`SearchIndexRequestConfiguration.cs`, по образцу существующих. `DbSet`-ы в `FlowDbContext` добавить как `internal`.

### 1.4 Миграция

Одна миграция `AddSearchIndex`:

```
dotnet ef migrations add AddSearchIndex --project src/Flow.Infrastructure --startup-project src/Flow.Api
```

В `Up` первой строкой — `migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");`, затем таблицы.
HNSW и GIN создавать **сырым SQL** (`CREATE INDEX ... USING hnsw (...) WITH (m = 16, ef_construction = 64)`),
EF их не умеет. `Down` — обратный порядок; расширение не удалять.

### 1.5 Конфигурация

`src/Flow.Api/appsettings.json` — секция `Search` по образцу базового ТЗ (раздел «Конфигурация»). В этой ветке
достаточно: `Enabled=false`, `Embeddings:{Provider=Http, QueryEndpoint, IndexingEndpoint, Model, Dimensions=512,
QueryInstruction, BatchSize=16, TimeoutSeconds=5}`, `Indexing:{Enabled=true, PollIntervalSeconds=5, BatchSize=32,
MaxAttempts=10, ChunkTokens=512, ChunkOverlap=64}`. Биндинг — `SearchOptions` через
`Microsoft.Extensions.Options.ConfigurationExtensions` (пакет уже подключён).

`.env.example` и `docker-compose.data.yml`: сервис `embeddings` под профилем `ai` с внешним томом `flow-models-data`,
переменные `EMBEDDINGS_QUERY_ENDPOINT`, `EMBEDDINGS_INDEXING_ENDPOINT`, `EMBEDDINGS_MODEL`, `EMBEDDINGS_PORT`,
`EMBEDDINGS_BIND`, `MODELS_ROOT`; `docker/data/init-env.sh` создаёт том. В `docker-compose.yml` сервис `api` получает
`Search__*` из этих переменных.

**Готово, когда:** `dotnet build Flow.slnx` и `dotnet test Flow.slnx` зелёные; миграция применяется на существующей
базе (доски `DBACK/DFRONT/DBRAND/DREC` и их задачи на месте) и откатывается; `Search:Enabled=false`, поведение API и
клиента не изменилось.

## Этап 2. Эмбеддер за абстракцией

### 2.1 Абстракция

`src/Flow.Application/Abstractions/IEmbeddingGenerator.cs`:

```csharp
public interface IEmbeddingGenerator
{
    /// <summary>Версия модели: {модель}:{размерность}:{хеш инструкции}. Векторы разных версий несовместимы.</summary>
    string ModelVersion { get; }

    int Dimensions { get; }

    /// <summary>Документы — без инструкции, как есть.</summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);

    /// <summary>Запрос — с Instruct-префиксом. Асимметрия обязательна, иначе качество падает молча.</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken ct);
}
```

### 2.2 Реализация

`src/Flow.Infrastructure/Search/HttpEmbeddingGenerator.cs` — два именованных `HttpClient`:
`embeddings-query` (таймаут из `TimeoutSeconds`, дефолт 5 с) и `embeddings-indexing` (60 с). `POST {endpoint}/embeddings`
с телом `{ "model": …, "input": [ … ] }`; пустой `IndexingEndpoint` означает «тот же, что `QueryEndpoint`».

Обязательная постобработка (`Search/Qwen3Embeddings.cs`, чистые функции — покрыть юнит-тестами):

1. Если сервер вернул полную размерность — **урезать до `Dimensions`** (MRL) и **перенормализовать** L2.
2. Если вернул ненормализованный вектор — нормализовать; проверка `|v| ≈ 1` с допуском.
3. Запрос оборачивается как `Instruct: {QueryInstruction}\nQuery: {q}`; документ — без обёртки.
4. `ModelVersion` = `$"{Model}:{Dimensions}:{SHA256(QueryInstruction)[..8]}"`.

Pooling по последнему токену делает сам `llama-server` для embedding-модели — в .NET его не реализуем, но тест на
нормализацию и размерность обязателен.

`src/Flow.Infrastructure/Search/FakeEmbeddingGenerator.cs` (или в тестовом проекте): детерминированный вектор от
хеша текста, `ModelVersion = "fake:512:test"`. Используется всеми интеграционными тестами — они не должны тянуть модель.

Регистрация — `AddFlowSearch(configuration)` в `Flow.Infrastructure/DependencyInjection`: биндит `SearchOptions`,
выбирает реализацию по `Provider` (пока только `Http`; `Onnx` → `NotSupportedException` с внятным текстом),
регистрирует `HttpClient`-ы. Вызов — в `Flow.Api/Program.cs` рядом с `AddFlowInfrastructure`.

### 2.3 Первый видимый эндпоинт

`src/Flow.Api/Controllers/SearchController.cs` — только `GET /search/status`, `[Authorize]`, права: Admin+
(`EnsureCanManageUsers` не подходит — добавить `EnsureCanViewSearchDiagnostics` в `IPermissionService` либо
использовать проверку роли `>= Admin` тем же способом, что остальные матрицы прав).

Запрос `SearchStatusQuery` возвращает `SearchStatusResponse` (контракт в `src/Flow.Shared/Contracts/Search/`):

```
{ enabled, embedderAvailable, modelVersion, dimensions,
  queueTotal, queueStuck, chunksByType: { task, comment, board, user },
  oldestQueuedAt }
```

При `Search:Enabled=false` — `200` с `enabled: false` и нулями, без похода к эмбеддеру.

**Готово, когда:** при поднятом `llama-server` `/search/status` показывает `embedderAvailable: true`,
`dimensions: 512`, `modelVersion`; при погашенном — `false`, и приложение работает как раньше.

## Этап 3. Индексация

### 3.1 Постановка в очередь

`src/Flow.Application/Abstractions/ISearchIndexQueue.cs`:

```csharp
public interface ISearchIndexQueue
{
    /// <summary>Ставит источник в очередь. Коммитится той же SaveChangesAsync, что и изменение сущности.</summary>
    void Enqueue(SearchSourceType sourceType, Guid sourceId, Guid? boardId, SearchIndexOperation operation, int priority = 0);
}
```

Реализация `src/Flow.Infrastructure/Search/SearchIndexQueue.cs` — scoped, поверх того же `FlowDbContext`
(как репозитории: `Add`, без собственного `SaveChanges`). Дубль по `(SourceType, SourceId, Operation)` разрешается
upsert-ом: обновить `EnqueuedAt` и взять минимальный `Priority`.

Точки вызова — в существующих хендлерах:

| Файл хендлера | Что ставить |
|---|---|
| `Tasks/Commands/TaskCreateCommand/TaskCreateCommandHandler.cs` | `Upsert` задачи |
| `Tasks/Commands/TaskUpdateCommand/TaskUpdateCommandHandler.cs` | `Upsert` — **только если реально изменились** `Title`/`Description` (там уже есть сравнение для `TaskActivity`); при смене статуса — обновить `IsClosed` чанков задачи без реэмбеддинга |
| `Tasks/Commands/TaskDeleteCommand/TaskDeleteCommandHandler.cs` | `Delete` задачи и её комментариев |
| `Tasks/Commands/TaskCommentAddCommand/…`, `TaskCommentEditCommand/…` | `Upsert` комментария (у `Edit` — только при изменившемся теле, там уже есть `bool` из домена) |
| `Tasks/Commands/TaskCommentDeleteCommand/…` | `Delete` комментария |
| `Boards/Commands/BoardCreateCommand/…`, `BoardRenameCommand/…` | `Upsert` доски |
| `Boards/Commands/BoardDeleteCommand/…` | `Delete` доски + всех её задач и комментариев (в `BoardRepository.RemoveAsync` задачи уже догружаются — использовать тот же список) |
| `Users/Commands/UserCreateCommand/…`, `UserUpdateProfileCommand/…`, `UserChangeUsernameCommand/…` | `Upsert` пользователя |
| `Users/Commands/UserDeactivateCommand/…` | `Delete` пользователя из индекса |
| `Users/Commands/UserActivateCommand/…` | `Upsert` пользователя |

`TaskAssignCommand` и `TaskSetDueDateCommand` индекс **не трогают** — текста не меняют.

### 3.2 Чанкинг

`src/Flow.Application/Features/Search/TextChunker.cs` — `ChunkTokens` / `ChunkOverlap`, резать по границам абзацев,
затем предложений; токены считать приближённо (символы ÷ 3 для русского) — точный токенизатор в этой ветке не нужен.

`ChunkHeaderBuilder.cs` — шапка, которая идёт **только в эмбеддинг**, не в `Content`:

| Тип | Шапка |
|---|---|
| Задача | `[PROJ-142] Падает экспорт отчёта в PDF` |
| Комментарий | `Проект «Имя» · PROJ-142 · комментарий` |
| Проект | `Проект «Имя» (PROJ)` |
| Человек | `@username · Имя Фамилия` |

Что во что превращается: задача — `Title` + `Description` (1..N чанков); комментарий — тело (обычно 1);
проект — `Name` + `Key` (1 чанк); человек — `Username`, ФИО, ссылки (1 чанк).

### 3.3 Воркер

`src/Flow.Infrastructure/Search/SearchIndexingWorker.cs` — `BackgroundService`, регистрируется из `AddFlowSearch`
при `Search:Enabled && Search:Indexing:Enabled`. Цикл:

1. `SELECT … FROM "SearchIndexQueue" WHERE "NextAttemptAt" <= now() ORDER BY "Priority", "EnqueuedAt" LIMIT @batch FOR UPDATE SKIP LOCKED` — своя транзакция, свой scope.
2. Прочитать источники, построить чанки, посчитать `ContentHash`.
3. Отбросить чанки, у которых `ContentHash` и `ModelVersion` совпали с уже лежащими в индексе. Для остальных —
   поискать готовый вектор по `ContentHash` + `ModelVersion` (переиспользование), остаток отправить батчем в эмбеддер.
4. Upsert чанков, удалить лишние (источник стал короче), удалить всё по источнику для `Delete`.
5. Успех — удалить запись очереди. Ошибка — `AttemptCount++`, `NextAttemptAt = now() + backoff` (5 с → 5 мин,
   экспоненциально), `LastError`; после `MaxAttempts` запись остаётся и попадает в `queueStuck`.

`SKIP LOCKED` обязателен: несколько экземпляров Api не должны мешать друг другу.

### 3.4 Переиндексация

`POST /search/reindex` — `[Authorize]`, только Owner, тело `{ types?: [...], boardId?: guid }`, ответ `202`.
`ReindexCommand` ставит в очередь всё подходящее пачками по 1000 с `Priority = 1` — живые правки (`Priority = 0`)
всегда идут раньше.

Чанки прошлой `ModelVersion` не удаляются сразу: отдельный шаг в конце прогона
(`DELETE FROM "SearchChunks" WHERE "ModelVersion" <> @current`).

**Готово, когда:** выполняются критерии приёмки ниже.

## Тесты

Юнит (`tests/Flow.Application.Tests`):

- `TextChunker`: границы абзацев, перекрытие, текст короче лимита, слово длиннее лимита, пустая строка.
- `ChunkHeaderBuilder`: по тесту на каждый из четырёх типов.
- `Qwen3Embeddings`: MRL-урезание сохраняет нормализацию; уже нормализованный вектор не портится; неверная
  размерность от сервера → внятное исключение.
- `ModelVersion` меняется при смене модели, размерности или инструкции.

Интеграционные (`tests/Flow.Infrastructure.Tests`, `FakeEmbeddingGenerator`):

- Миграция `AddSearchIndex` применяется и откатывается на чистой базе.
- Создание задачи → запись в очереди в той же транзакции; откат транзакции → очередь пуста.
- Прогон воркера: очередь пустеет, чанки с векторами появляются, `Tsv` заполнен.
- Повторный прогон без изменений текста — эмбеддер **не вызывается** (счётчик вызовов у фейка).
- Правка `Title` → чанки перестроены; правка срока или исполнителя → очередь не пополняется.
- Удаление задачи, комментария, доски → чанки исчезают; удаление доски убирает и чанки её задач.
- Два воркера параллельно на одной очереди не обрабатывают одну запись дважды (`SKIP LOCKED`).
- Недоступный эмбеддер: `AttemptCount` растёт, `LastError` заполнен, API продолжает работать.
- `reindex` по существующим данным наполняет индекс всеми четырьмя типами.
- Приоритеты: запись `Priority = 0`, добавленная после массового `reindex`, обрабатывается раньше.

Реальная модель — `[Trait("Category", "Model")]`, из CI исключена; проверяет размерность 512, нормализацию и что
«запрос ↔ релевантный документ» ближе, чем «запрос ↔ случайный».

**`dotnet test Flow.slnx` должен быть зелёным без доступной модели эмбеддингов.**

## Критерии приёмки ветки

1. `dotnet build Flow.slnx` и `dotnet test Flow.slnx` зелёные; CI (`build.yml`, `compose.yml`, `docker.yml`) проходит.
2. Миграция применяется на существующей базе без потери данных и откатывается.
3. При `Search:Enabled=false` (дефолт в репозитории) поведение всего существующего API и клиента идентично
   состоянию до ветки; воркер не стартует; `/search/status` отвечает `enabled: false`.
4. При `Search:Enabled=true` и поднятом `llama-server`: `POST /search/reindex` наполняет индекс по четырём тестовым
   доскам — в `/search/status` видны непустые `chunksByType` по всем четырём типам.
5. Создание задачи или комментария приводит к появлению чанков не позже 30 с; удаление — к их исчезновению.
6. Остановка эмбеддера не приводит к 5xx ни на одном существующем эндпоинте; очередь копится, `queueStuck` растёт.
7. `IsClosed` в чанках соответствует финальности статуса задачи после смены статуса (без реэмбеддинга — проверить,
   что эмбеддер не вызывался).
8. README / README.ru / AGENTS.md дополнены: новый образ Postgres, секция `Search`, сервис `embeddings`,
   что такое `/search/status` и `/search/reindex`.

## Чего в этой ветке быть не должно

- `GET /search`, RRF, гибридный SQL — этап 4.
- Любые изменения в `Flow.Client` — этап 5.
- `QueryIntentParser`, `/tasks/{id}/similar` — этап 6.
- `ITextExtractor`, PdfPig, OpenXml, VL-модель, реранкер — этапы 7–9.
- Партиционирование, `Flow.Worker`, разделение CPU/GPU — по триггерам из раздела «Монолитность» базового ТЗ.
- Изменения в `Flow.Domain`: индекс — проекция, домен не трогаем.
