# Контекст диалога: пайплайн «описание задачи → действия → код» (Tree-sitter)

Краткое сводное контекстное досье, чтобы другая модель могла продолжить работу без исходного чата.

## 1. Что такое Flow (проект)
- C#, .NET 10, трекер задач по проектам (CRM). Слойная архитектура: `Flow.Api` (backend и хост интерфейса), `Flow.Auth` (RCL: Identity + OpenIddict + BCrypt), `Flow.Auth.Contracts`, `Flow.Shared` (DTO), `Flow.Application` (MediatR-хендлеры, доменные инварианты, права), `Flow.Domain` (сущности), `Flow.Infrastructure` (EF Core/Postgres+pgvector, S3, поиск), `Flow.Client` (Razor-библиотека, Blazor Interactive Server).
- Зависимости: `Application → Domain + Shared + Auth.Contracts`; `Infrastructure → Application`; `Auth → Auth.Contracts`; `Api → Application + Infrastructure + Auth + Shared`.
- Ключи: build/test — `dotnet build Flow.slnx`, `dotnet test Flow.slnx`. Требуется .NET SDK 10.x (global.json, rollForward latestMinor). Postgres 16 + pgvector, S3 = MinIO.

## 2. Исходная интеграция и текущее состояние (агенты / OpenCode)

Исторический срез первых коммитов:
- `960d1ff` — добавлен `docker/opencode/opencode.jsonc`: модель `lmstudio/qwen3.6-35b-a3b`, провайдер openai-compatible → `http://host.docker.internal:1234/v1`.
- `ea81ba5` — базовый HTTP-доступ к OpenCode + тестовый интерфейс:
  - `src/Flow.Infrastructure/OpenCode/OpenCodeClient.cs` — HTTP-клиент V2 API (создание сессии → prompt → wait → чтение ответа). `OpenCodeOptions` (`OpenCode`): BaseUrl `http://localhost:4096`, провайдер/model как в конфиге, таймаут 300с.
  - `src/Flow.Application/Abstractions/IFlowAgentClient.cs` — интерфейс + `OpenCodeAnswer(SessionId, Text)`.
  - `src/Flow.Application/Features/Agents/Commands/AgentTestAskCommand/` — первоначально принимал рабочий каталог; теперь принимает `RepositoryId`, проверяет видимость привязанного проекта и получает путь готовой ревизии на сервере.
  - `src/Flow.Api/Controllers/AgentsController.cs` — актуальный HTTP-маршрут `POST /api/agents/test` (`{repositoryId, question}`) → 200; ошибки → 400/502.
  - `src/Flow.Client/Pages/AgentTest.razor` (+ `.css`) — тестовый UI.
- Текущее состояние: тестовая форма `/agents/test` работает с выбранным проектом и синхронизированным репозиторием, ответ отображается как Markdown. Учётные данные OpenCode задаются в `.env` и передаются Compose обоим сервисам; локальные значения по умолчанию остаются в `OpenCodeOptions`. Каждому вопросу соответствует отдельная сессия; ответ в задачу не сохраняется.

## 3. Как запустить стек
- Актуальная пошаговая инструкция — [OpenCode_setup.md](OpenCode_setup.md): Linux/macOS, Windows, Rider, LM Studio и общий том исходного кода.
- Основное: подготовить `.env`, затем `sh docker/up.sh` (Windows: `docker/up.ps1`). Flow http://localhost:8080, форма http://localhost:8080/agents/test, начальный вход `admin@flow.com`/`admin` с обязательной сменой пароля. Миграции применяются автоматически; отдельного frontend на 5016 нет.
- OpenCode запускается вместе с PostgreSQL/S3 обычным `docker compose -f docker-compose.data.yml up -d`, без профиля `agent`. Отдельно: `docker compose -f docker-compose.data.yml up -d opencode`. Порт на хосте — 127.0.0.1:4096; Flow.Api в Docker ходит по `http://opencode:4096`.
- LM Studio запускается отдельно на хосте на 1234; OpenCode использует `http://host.docker.internal:1234/v1`. Провайдер и модель настраиваются в `docker/opencode/opencode.jsonc` и согласуются с `.env`.
- Перед первым запуском через Rider init выполняется вручную вариантом под свою ОС. Для готовой схемы с общим томом использовать `Flow (data stack)` → `Flow Only` (Debug для отладчика); `Flow (api debug)` работает на хосте и требует отдельного согласования хранилища исходного кода.
- Умный поиск (эмбеддер/реранкер) — профиль `ai`, опционально; веса качает `docker/data/pull-models.sh`.

## 4. Задача: пайплайн декомпозиции описания аналитика в код
Цель: по описанию задачи (текст от аналитика) разложить её на атомарные действия и сопоставить каждое с реальными функциями/процедурами в коде.

### Ключевая проблема — grounding (заземление на реальный код)
LLM не должен выдумывать соответствия; нужно индексировать реальные символы кода и искать по ним.

### Архитектура пайплайна (4 стадии)
1. **Инвентаризация кода** — symbol index: для каждого языка извлечь определения функций/процедур {language, kind, name, filePath, startLine, endLine, signature?}.
2. **Декомпозиция** — OpenCode/LM Studio разбивает текст на список атомарных действий со схемой `{id, глагол, объект, ожидаемое поведение, права/роль}`.
3. **Сопоставление (grounding)** — эмбедим запрос действия, ищем top-K символов по косинусу, LLM-рейнкер подтверждает/отвергает с цитатой из кода.
4. **Анализ покрытия** — итоговый отчёт: mapped (действие → символ + confidence + ссылка), gaps (действий без кода = «писать новый код»), conflicts (частичное покрытие).

### Переиспользование из текущего кода
- `IFlowAgentClient` + OpenCode/LM Studio — готовый доступ к модели.
- Поисковый индекс (`ISearchIndexQueue`, эмбеддер, RRF) — как бэкенд поиска по символам (новый `SourceType=CodeSymbol`).

## 5. Языковой подход: Tree-sitter (выбрано)
- Roslyn **не подходит** — проекты мультиязыковые.
- **Tree-sitter** — единый парсер для 90+ языков с общей моделью данных и общим языком S-запросов. Единая схема символов для всех языков, резка по границам функций/процедур.

### Варианты реализации (обсуждалось)
1. **.NET-бинды** (`TreeSitter` core + пакеты грамматик `tree-sitter-python`, `tree-sitter-c-sharp` и т.д.) — рекомендовано, вписывается в Infrastructure/DI, без нового Docker-сервиса.
2. Python-сайдкар — больше грамматик из коробки, но отдельный контейнер + IPC.
3. Эвристика + существующий векторный индекс — ноль зависимостей, но неточные границы.

### Варианты инвентаризации (альтернативы)
- Tree-sitter (выбрано).
- LSP (языковые серверы) — точно, но тяжело (сервер на язык).
- Эвристика по границам блоков — просто, неточно.

## 6. Текущее решение / следующие шаги
- Решение: **чистый .NET + Tree-sitter**. Первый спайк — один язык (Python), экстракция функций по S-запросу, единый рекорд `CodeSymbol(Language, Kind, Name, FilePath, StartLine, EndLine, Signature?)`. Спайк отдельно (console в tests), не тащить в библиотеку.
- Дальше: расширить грамматики до набора языков → `ISourceIndexer`/`IFileWalker` в Infrastructure → персистентность (`CodeSymbols` или `SourceType=CodeSymbol` в поисковом индексе) → маппинг действий через RRF/косинус + LLM-рейнкер.

### Открытые вопросы (нужны ответы от автора)
1. Идём по .NET-биндам Tree-sitter? (проверить актуальные имена/версии пакетов на nuget.org).
2. Какие языки в первом круге? (перечислить реальные языки проектов → подобрать грамматики).

---
Файл создан для передачи контекста другой модели. Источник — чат от 2026-09-22; описание запуска и тестовой интеграции актуализировано 2026-10-03. Разделы Tree-sitter описывают обсуждавшийся план, а не реализованную индексацию кода.
