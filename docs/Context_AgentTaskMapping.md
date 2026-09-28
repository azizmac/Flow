# Контекст диалога: пайплайн «описание задачи → действия → код» (Tree-sitter)

Краткое сводное контекстное досье, чтобы другая модель могла продолжить работу без исходного чата.

## 1. Что такое Flow (проект)
- C#, .NET 10, трекер задач по проектам (CRM). Слойная архитектура: `Flow.Api` (backend-хост), `Flow.Auth` (RCL: Identity + OpenIddict + BCrypt), `Flow.Auth.Contracts`, `Flow.Shared` (DTO), `Flow.Application` (MediatR-хендлеры, доменные инварианты, права), `Flow.Domain` (сущности), `Flow.Infrastructure` (EF Core/Postgres+pgvector, S3, поиск), `Flow.Client` (Blazor WASM).
- Зависимости: `Application → Domain + Shared + Auth.Contracts`; `Infrastructure → Application`; `Auth → Auth.Contracts`; `Api → Application + Infrastructure + Auth + Shared`.
- Ключи: build/test — `dotnet build Flow.slnx`, `dotnet test Flow.slnx`. Требуется .NET SDK 10.x (global.json, rollForward latestMinor). Postgres 16 + pgvector, S3 = MinIO.

## 2. Что сделано в последних 2 комитах (агенты / OpenCode)
- `960d1ff` — добавлен `docker/opencode/opencode.jsonc`: модель `lmstudio/qwen3.6-35b-a3b`, провайдер openai-compatible → `http://host.docker.internal:1234/v1`.
- `ea81ba5` — базовый HTTP-доступ к OpenCode + тестовый интерфейс:
  - `src/Flow.Infrastructure/OpenCode/OpenCodeClient.cs` — HTTP-клиент V2 API (создание сессии → prompt → wait → чтение ответа). `OpenCodeOptions` (`OpenCode`): BaseUrl `http://localhost:4096`, провайдер/model как в конфиге, таймаут 300с.
  - `src/Flow.Application/Abstractions/IFlowAgentClient.cs` — интерфейс + `OpenCodeAnswer(SessionId, Text)`.
  - `src/Flow.Application/Features/Agents/Commands/AgentTestAskCommand/` — хендлер резолвит actor и **ограничивает workspace только `/workspaces/*`**.
  - `src/Flow.Api/Controllers/AgentsController.cs` — `POST /agents/test` (`{workspaceDirectory, question}`) → 200; ошибки → 400/502.
  - `src/Flow.Client/Pages/AgentTest.razor` (+ `.css`) — тестовый UI.
- Статус: рабочий «тестовый» канал из UI в OpenCode, захардкоженные учётные данные, нет истории сессий и нормального API-контракта.

## 3. Как запустить стек
- Основное (бэкенд + фронт + БД + S3): `cp .env.example .env && sh docker/up.sh`. Клиент http://localhost:5016, вход `admin@flow.com`/`admin`. Миграции применяются автоматически.
- OpenCode (отдельно, профиль agent в data-стеке): `docker compose -f docker-compose.data.yml --profile agent up -d opencode`. Работает на 127.0.0.1:4096. Flow.Api ходит по `http://opencode:4096` (настройки из `.env`: OPENCODE_API_URL, OPENCODE_PROVIDER_ID, OPENCODE_MODEL_ID).
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
Файл создан для передачи контекста другой модели. Источник — чат от 2026-09-22.
