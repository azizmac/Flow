# Запуск Flow с OpenCode и LM Studio

## Состав стека

- `docker-compose.data.yml` (проект `flow-data`) запускает PostgreSQL, MinIO, создание бакета и OpenCode. Для OpenCode не нужен отдельный профиль.
- `docker-compose.yml` запускает сервис `api`: backend, авторизацию и интерфейс Blazor Interactive Server в одном контейнере. Отдельного frontend-контейнера нет.
- LM Studio с моделью Qwen работает на машине разработчика, вне Compose. OpenCode запускается командой `serve` и обращается к её OpenAI-совместимому API.
- Общая сеть — `flow-network`, общий том исходного кода — `flow-repository-workspaces`.

Адрес Flow по умолчанию — `http://localhost:8080`, тестовая форма — `http://localhost:8080/agents/test`. Порт 5016 относится к прежнему WebAssembly-клиенту: оставшийся старый контейнер `flow-client` не нужно использовать для проверки текущего приложения.

## 1. Подготовить окружение

Нужен Docker с Compose. Для ответов агента дополнительно нужны LM Studio и загруженная модель. Все команды выполняются из корня Flow.

Если `.env` ещё нет, скопировать `.env.example`:

```bash
# Linux/macOS
cp .env.example .env
```

```powershell
# Windows
Copy-Item .env.example .env
```

Существующий `.env` не перезаписывать. Проверить порты и пароли: если локальный PostgreSQL уже занимает 5432, задать `POSTGRES_PORT=5433`. Настройка окружения — ответственность разработчика.

Основные настройки OpenCode в `.env`:

| Переменная | Значение по умолчанию | Назначение |
|---|---|---|
| `OPENCODE_IMAGE` | `ghcr.io/anomalyco/opencode:2.0.0` | Версия сервера |
| `OPENCODE_BIND` / `OPENCODE_PORT` | `127.0.0.1` / `4096` | Доступ к OpenCode с хоста |
| `OPENCODE_SERVER_USERNAME` / `OPENCODE_SERVER_PASSWORD` | `opencode` / `flow-opencode-local` | Учётные данные HTTP API; Compose передаёт их и OpenCode, и Flow |
| `OPENCODE_API_URL` | `http://opencode:4096` | Адрес OpenCode для Flow.Api внутри Docker |
| `OPENCODE_PROVIDER_ID` / `OPENCODE_MODEL_ID` | `lmstudio` / `qwen3.6-35b-a3b` | Провайдер и ключ модели из конфигурации OpenCode |
| `OPENCODE_TIMEOUT_SECONDS` | `300` | Таймаут ответа агента |

Пароли по умолчанию предназначены для локального стенда. Секреты не коммитить; порт OpenCode не открывать наружу без необходимости.

## 2. Подключить LM Studio

1. Загрузить модель Qwen в LM Studio и запустить API-сервер на порту 1234.
2. Проверить доступ с хоста к `http://localhost:1234/v1/models` и идентификатор нужной модели в ответе.
3. Обеспечить доступ к серверу из Docker. В [настройках LM Studio](https://lmstudio.ai/docs/developer/core/server/settings) для этого есть `Serve on Local Network`. На Linux сервер, слушающий только `127.0.0.1`, недоступен через Docker host gateway. Сетевой доступ разрешать только доверенным источникам через firewall; эта настройка может открыть порт и другим устройствам.
4. Проверить `docker/opencode/opencode.jsonc`: `baseURL` — `http://host.docker.internal:1234/v1`. Compose добавляет `host.docker.internal` через `host-gateway`; `localhost` внутри контейнера означал бы сам OpenCode, а не LM Studio.

В конфигурации три разных идентификатора: провайдер `lmstudio`, ключ модели `qwen3.6-35b-a3b` и фактический `modelID` — `qwen/qwen3.6-35b-a3b`. Последний должен совпадать с моделью, доступной через API LM Studio. При замене модели согласовать конфигурацию OpenCode и `OPENCODE_PROVIDER_ID` / `OPENCODE_MODEL_ID` в `.env`.

Текущий файл OpenCode не задаёт токен LM Studio. Если включён `Require Authentication`, необходимо отдельно настроить передачу токена провайдером OpenCode. `OPENCODE_SERVER_PASSWORD` защищает другой участок — Flow → OpenCode — и не является токеном LM Studio. CORS для серверного запроса OpenCode → LM Studio не нужен.

После изменения конфигурации OpenCode:

```bash
docker compose -f docker-compose.data.yml restart opencode
```

После изменения `.env` применить настройки через `up -d` соответствующего стека: простой `restart` не обновляет переменные окружения контейнера.

## 3. Создать сеть и тома, запустить контейнеры

Linux/macOS:

```bash
sh docker/data/init-env.sh
docker compose -f docker-compose.data.yml up -d
docker compose up -d --build
```

Windows (PowerShell):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docker/data/init-env.ps1
docker compose -f docker-compose.data.yml up -d
docker compose up -d --build
```

Init создаёт сеть и четыре внешних тома: `flow-postgres-data`, `flow-minio-data`, `flow-models-data`, `flow-repository-workspaces`. Он не запускает контейнеры; повторный вызов безопасен. Ошибка `external volume ... not found` означает, что этот шаг ещё не выполнен.

Альтернатива тем же шагам — `sh docker/up.sh` или `powershell -ExecutionPolicy Bypass -File docker/up.ps1`: эти скрипты сами вызывают init и поднимают оба стека. LM Studio они не запускают.

### Запуск из Rider

Перед первым стартом вручную выполнить `Flow (init Linux/macOS)` или `Flow (init Windows)`. Если путь интерпретатора отличается, настроить локальную копию конфигурации под свою машину. `.run` — удобство Rider, а не обязательное условие запуска; команды выше доступны и из терминала Visual Studio.

- `Flow (data stack)` — PostgreSQL, MinIO, создание бакета и OpenCode.
- `Flow (full stack)` — сначала data stack, затем сборка и запуск приложения. Init автоматически не вызывается.
- `Flow Only` — только приложение на уже работающем data stack. Подключает `docker-compose.debug.yml`: сборка Debug, окружение Development. Для отладчика использовать действие Debug; Fast mode выключен, чтобы выполнялись все стадии Dockerfile, включая CodeMirror.
- `Flow (api debug)` — data stack в Docker, Flow.Api процессом на хосте на `http://localhost:5000`. `.env` Compose не становится автоматически настройками этого процесса. Адрес OpenCode с хоста — `http://localhost:4096` (с учётом `OPENCODE_PORT`), но рабочий каталог `./repository-workspaces` не является Docker-томом: для анализа клонированного кода нужно отдельно согласовать хранилище с `/workspaces` OpenCode. Готовый вариант с общим томом — `Flow Only`.

## 4. Синхронизировать репозиторий и задать вопрос

1. Открыть Flow на `http://localhost:8080`, войти и при первом входе сменить начальный пароль.
2. В «Настройки → Интеграции» зарегистрировать подключение к Git-хостингу и репозиторий. Управление подключениями требует глобальной роли Admin или Owner.
3. Создать или выбрать проект, открыть `/boards/{boardId}/repositories` и привязать репозиторий. Для привязки и синхронизации нужно право проекта `ManageGit`.
4. Нажать «Синхронизировать». Клонирование для агента поддерживает публичные HTTPS-репозитории GitHub и GitLab; наличие подключения к хостингу само по себе не даёт клонированию доступ к приватным репозиториям.
5. После состояния `Ready` и появления commit нажать «Спросить агента» либо открыть `/agents/test`, выбрать проект и репозиторий.
6. Отправить вопрос «Что хранится в этом репозитории?». Ответ отображается как Markdown; показанный идентификатор относится к сессии OpenCode. Ответ не записывается в задачу.

Flow.Api клонирует репозиторий в общий том: `/var/lib/flow/repositories/<repositoryId>/<commit>`. OpenCode видит ту же ревизию как `/workspaces/<repositoryId>/<commit>`; `repositoryId` в пути записан без дефисов. Том у Flow доступен для записи, у OpenCode — `read-only`. Путь для запроса формирует сервер, вручную вводить его в форму не нужно.

Вызов агента требует видимости хотя бы одного проекта, к которому привязан репозиторий. Если повторная синхронизация завершилась ошибкой, но осталась ранее готовая ревизия, форма может анализировать её; выбранный commit виден рядом с вопросом.

## 5. Диагностика

```bash
docker compose -f docker-compose.data.yml ps
docker compose ps
docker compose -f docker-compose.data.yml logs --tail 100 opencode
docker compose logs --tail 100 api
docker compose -f docker-compose.data.yml exec opencode ls /workspaces
```

- OpenCode отдельно можно поднять командой `docker compose -f docker-compose.data.yml up -d opencode`; профиль `agent` не используется.
- Пустой `/workspaces` или отсутствие нужной ревизии — проверить синхронизацию и общий том, а не копировать репозиторий вручную в контейнер.
- Отказ соединения с LM Studio — проверить запущенный API-сервер, порт 1234, сетевое прослушивание и firewall. Доступ с хоста ещё не доказывает доступ из контейнера.
- Ошибка авторизации OpenCode — проверить совпадение его учётных данных и настроек Flow. Ошибка авторизации LM Studio — проверить токен провайдера.
- После перехода с прежнего frontend использовать порт 8080, а не оставшийся контейнер на 5016. HTTP 200 означает загрузку документа, но не гарантирует успешный запуск Blazor; подробности ошибки смотреть в Console браузера и логах API.
- Профиль `ai` относится к эмбеддингам поиска и не нужен для ответов OpenCode через LM Studio.
