# ТЗ: модульный монолит — Flow.Auth как модуль Flow.Api

Статус: **реализовано**. Этапы 0–5 завершены; документ фиксирует причины и итоговую архитектуру перехода.

## Исходная картина

Backend собран из двух самостоятельных сервисов: ядро `Flow.Api` (БД `flow`) и аутентификация `Flow.Auth`
(БД `flow_auth`). Связь только по HTTP: Flow.Api создаёт и меняет учётные записи через admin-API `/accounts`
(client_credentials, клиент `flow-api`, scope `auth:admin`), валидирует JWT через discovery/JWKS Auth
(`Auth:BaseUrl`, `IssuerRewritingConfigurationRetriever`), username/email продублированы в двух базах.
Исходное сервисное ТЗ — `docs/TZ_auth.md`.

## Цель

Один backend-процесс (модульный монолит): хост `Flow.Api` + Auth-модуль + ядро. Внутреннее общение —
in-process, без admin-API и client_credentials между своими же сервисами. Внешняя форма не меняется:
клиент ходит на тот же адрес, OAuth2/OIDC остаётся.

## Принятые решения

- **OpenIddict остаётся.** Браузерный вход — code + PKCE; на будущее — машинные клиенты (система-робот)
  через client_credentials. Токен-модель и JWT-валидация в этом ТЗ не пересматриваются.
- **Cookie-аутентификация браузера не делается.** Вопрос отложен: куки требуют same-origin и не закрывают
  задачу машинных клиентов, а OpenIddict уже есть.
- **Одна БД `flow`, у модулей свои схемы**: таблицы ядра — в `public`, Auth-модуля (Identity и OpenIddict) —
  в схеме `auth`; у каждого контекста своя история миграций. База `flow_auth` больше не используется.
- **`Flow.Auth` становится Razor Class Library** (`src/Flow.Auth`) и подключается к хосту одним вызовом
  `AddAuthModule(...)`; страницы, контроллеры и статические файлы модуля работают из-под хоста.
- **Учётные записи — in-process.** `IAccountService` реализуется в Auth-модуле поверх `UserManager`;
  admin-API `/accounts`, клиент `flow-api`, scope `auth:admin`, `ClientCredentialsTokenProvider` и
  маппинг недоступности Auth в 502 удаляются.
- **Хост остаётся `Flow.Api`** (имя проекта не меняется).
- **Service accounts/роботы — отдельный этап**, вне этого ТЗ. Задел: client_credentials и user-актор
  (профиль с признаком «сервисный») обсуждаются там.
- **Вертикальный сплит ядра не делается**: Boards/Tasks/Users остаются одним модулем в текущих слоях
  Domain/Application/Infrastructure.
- Зависимость односторонняя: ядро знает Auth только через контракт (`IAccountService`), Auth о ядре не знает.

## Целевая схема

```
Flow.Api (единственный backend-процесс)
 ├─ Flow.Auth (RCL): Identity + BCrypt, OpenIddict server, /connect/*, /account/*, AuthDbContext (схема auth)
 ├─ ядро: Domain/Application/Infrastructure (Boards, Tasks, Users; flow)
 └─ Flow.Shared (DTO для клиента)
Flow.Client (Blazor WASM) → один адрес backend'а
Docker: api + client (+ data stack без изменений)
```

Границы: Auth-модуль не обращается к репозиториям и таблицам ядра, ядро не обращается к `AuthDbContext`
напрямую — только через `IAccountService`.

## Что удалено

| Что | Где |
|---|---|
| HTTP-клиент admin-API и token provider | `src/Flow.Infrastructure/Auth/AuthAccountService.cs`, `AuthClientOptions.cs`, `ClientCredentialsTokenProvider.cs` |
| Admin-API и клиент `flow-api` | `src/Flow.Auth/Controllers/AccountsController.cs`, `ClientSeeder`, scope `auth:admin`, audiences `flow-auth` |
| Самостоятельный хост Auth | `src/Flow.Auth/Program.cs`, `Dockerfile`, `appsettings.json`, `launchSettings.json` |
| JwtBearer через discovery Auth (этап 4) | `Auth/JwtAuthOptions.cs`, `Auth/IssuerRewritingConfigurationRetriever.cs`, `Auth:BaseUrl` |
| Маппинг 502 | `Auth/ApiExceptionFilter.cs`, `AuthUnavailableException` |

## Этапы

| # | Содержание | Проверка |
|---|---|---|
| 0 | Документ и baseline | build + все тесты зелёные |
| 1 | Auth: `AddAuthModule`, тонкий `Program.cs` (поведение не меняется) | build + тесты |
| 2 | RCL + подключение к Flow.Api, таблицы Auth — в схеме `auth`, compose/CI/тесты | логин/OIDC из-под хоста, docker api+client |
| 3 | In-process учётки, удаление admin-API и client_credentials-обвязки | `POST /users` без HTTP, вход новым пользователем |
| 4 | Локальная валидация токенов (`OpenIddict.Validation.UseLocalServer`), отказ от JwtBearer+discovery | уходит `Auth:BaseUrl`, тесты auth |
| 5 | Чистка дублей, `AGENTS.md`, README, `.run` | `docker/up.sh` с нуля, первый вход |

## Вне области

Cookie-аутентификация браузера, service accounts/роботы, вертикальный сплит ядра,
`MapIdentityApi`, переезд клиента с WASM, приглашения/восстановление пароля.
