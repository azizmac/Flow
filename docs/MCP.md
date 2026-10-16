# MCP-сервер Flow

Flow отдаёт свои задачи агентам и IDE по [Model Context Protocol](https://modelcontextprotocol.io): Claude Code,
Claude Desktop, Cursor, VS Code и любой другой клиент MCP работает с проектами и задачами от имени пользователя
и с его правами.

- Адрес — `{AUTH_ISSUER}/mcp` (локально `http://localhost:8080/mcp`, при `dotnet run` — `http://localhost:5000/mcp`).
- Транспорт — Streamable HTTP, без состояния между запросами: балансировщику привязка к экземпляру не нужна.
- Отдельного сервиса нет: сервер живёт в `Flow.Api` (`src/Flow.Api/Mcp`), как контроллеры — поверх `IMediator`.
  Права, приватные проекты, workflow, журнал и очередь поиска работают ровно так же, как в интерфейсе и API.

## Подключение

Вход — OAuth 2.1 (code + PKCE) через встроенный OpenIddict, по спецификации MCP: на `401` сервер отвечает
`WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp"`, клиент сам находит
сервер авторизации (`/.well-known/oauth-authorization-server`) и открывает в браузере обычную страницу входа Flow.

Динамической регистрации клиентов у OpenIddict нет, поэтому клиенту MCP нужно назвать готового клиента
`flow-mcp` (публичный, без секрета). Scope (`flow-api offline_access`) клиенты берут из метаданных ресурса сами.
Access token живёт час (`Auth:AccessTokenLifetimeMinutes`), refresh — 14 дней; клиент обновляет его сам.

OpenIddict сверяет адрес возврата строкой целиком, вместе с портом и путём. Разрешённые адреса —
`Auth:McpClient:RedirectUris` в `appsettings.json`; там уже стоят адреса клиентов ниже:

| Клиент | Адрес возврата |
|---|---|
| Claude Code, Gemini CLI, Codex | `http://localhost:33418/callback` (и `127.0.0.1`) — порт задаётся в клиенте |
| VS Code | `http://127.0.0.1:33418`, `https://vscode.dev/redirect` |
| OpenCode | `http://127.0.0.1:19876/mcp/oauth/callback` — его адрес по умолчанию |
| Cursor | `cursor://anysphere.cursor-mcp/oauth/callback` |
| Claude (веб, Desktop, мобильный) | `https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback` |

Ниже `https://flow.example.com/mcp` — адрес вашего Flow (`{AUTH_ISSUER}/mcp`); локально — `http://localhost:8080/mcp`.
Хост и порт должны совпадать с `AUTH_ISSUER` буквально: `localhost` и `127.0.0.1` — разные адреса для `resource`.

### Claude Code

```bash
claude mcp add --transport http flow https://flow.example.com/mcp \
  --client-id flow-mcp --callback-port 33418
```

Затем `/mcp` → `flow` → Authenticate. Если у пользователя обязательная смена пароля, сначала будет она.

### OpenCode

OpenCode 2.x (в стеке Flow — `ghcr.io/anomalyco/opencode:2.0.0`), `opencode.jsonc` проекта или `~/.config/opencode/opencode.jsonc`:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "servers": {
      "flow": {
        "type": "remote",
        "url": "https://flow.example.com/mcp",
        "oauth": {
          "client_id": "flow-mcp",
          "scope": "flow-api offline_access",
          "redirect_uri": "http://127.0.0.1:19876/mcp/oauth/callback"
        }
      }
    }
  }
}
```

OpenCode 1.x — `opencode.json`, сервер прямо в `mcp`, поля в camelCase:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "flow": {
      "type": "remote",
      "url": "https://flow.example.com/mcp",
      "oauth": { "clientId": "flow-mcp", "scope": "flow-api offline_access" }
    }
  }
}
```

Вход — `opencode mcp auth flow` (или сам при первом вызове), проверка — `opencode mcp list`, выход — `opencode mcp logout flow`.
Токены лежат в `~/.local/share/opencode/mcp-auth.json`.

OpenCode из `docker-compose.data.yml` (`opencode serve` для `/agents/test`) — безголовый: браузера для входа у него нет.
Ему годится только готовый токен в `headers` (см. «Готовый токен»), и тот живёт час.

### VS Code (Copilot, агентный режим)

`.vscode/mcp.json` или пользовательский `mcp.json` (команда «MCP: Open User Configuration»):

```json
{
  "servers": {
    "flow": {
      "type": "http",
      "url": "https://flow.example.com/mcp",
      "oauth": { "clientId": "flow-mcp" }
    }
  }
}
```

Старые версии VS Code без поля `oauth` сами спросят client ID, когда увидят, что регистрации нет: ввести `flow-mcp`,
секрет оставить пустым.

### Cursor

`~/.cursor/mcp.json` или `.cursor/mcp.json` проекта:

```json
{
  "mcpServers": {
    "flow": {
      "url": "https://flow.example.com/mcp",
      "auth": { "CLIENT_ID": "flow-mcp", "scopes": ["flow-api", "offline_access"] }
    }
  }
}
```

Затем Settings → MCP → flow → Login. Cursor менял адрес возврата между версиями: если вход падает с `invalid_request`,
адрес, который он прислал, виден в строке браузера (`redirect_uri=`), — его нужно добавить в `Auth:McpClient:RedirectUris`.

### Gemini CLI

`~/.gemini/settings.json` или `.gemini/settings.json` проекта:

```json
{
  "mcpServers": {
    "flow": {
      "httpUrl": "https://flow.example.com/mcp",
      "oauth": {
        "enabled": true,
        "clientId": "flow-mcp",
        "scopes": ["flow-api", "offline_access"],
        "redirectUri": "http://localhost:33418/callback"
      }
    }
  }
}
```

Вход — `/mcp auth flow` внутри Gemini CLI.

### Codex CLI

`~/.codex/config.toml` или `.codex/config.toml` проекта:

```toml
[mcp_servers.flow]
url = "https://flow.example.com/mcp"
oauth = { client_id = "flow-mcp", callback_port = 33418, callback_url = "http://localhost:33418/callback" }
```

Вход — `codex mcp login flow`.

### Claude (веб, Desktop, мобильный)

Настройки → Connectors → Add custom connector: URL `https://flow.example.com/mcp`, в Advanced settings — OAuth Client ID
`flow-mcp`, секрет пустой. Коннектор подключают серверы Anthropic, поэтому Flow должен быть доступен из интернета по HTTPS;
`localhost` так не подключить — для локального Flow берите Claude Code.

### Готовый токен (любой клиент без OAuth)

Заголовок `Authorization: Bearer <access_token>` — тот же токен, что для `/api` (scope `flow-api`). Живёт час,
сам не обновляется, поэтому годится для скриптов и проверки, а не для постоянной работы. Пример для OpenCode 2.x:

```jsonc
"flow": {
  "type": "remote",
  "url": "http://api:8080/mcp",
  "headers": { "Authorization": "Bearer {env:FLOW_TOKEN}" }
}
```

### Проверка

```bash
curl -i -X POST https://flow.example.com/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'               # 401 + resource_metadata
curl https://flow.example.com/.well-known/oauth-protected-resource/mcp
curl https://flow.example.com/.well-known/oauth-authorization-server
```

Интерактивно — `npx @modelcontextprotocol/inspector`: Streamable HTTP, URL `…/mcp`, client ID `flow-mcp`.

| Симптом | Причина |
|---|---|
| `invalid_request` на шаге входа | адрес возврата клиента не в `Auth:McpClient:RedirectUris` (другой порт или путь) |
| `invalid_target` | URL MCP не совпадает с `{AUTH_ISSUER}/mcp` — другой хост, порт или лишний `/` |
| 401 с токеном | у токена нет scope `flow-api`, он истёк, или пользователь деактивирован |
| «Проект … не найден» | проект приватный, и пользователь в нём не участвует |

## Инструменты

| Инструмент | Что делает |
|---|---|
| `whoami` | Профиль текущего пользователя |
| `list_projects` | Видимые проекты: ключ, имя, число задач |
| `get_project` | Статусы, типы задач и пользовательские поля проекта |
| `search_tasks` | Отбор задач: проект, исполнитель, подстрока, FQL; страницы через `offset` |
| `get_task` | Карточка: описание, статус, люди, даты, оценка, чек-лист, комментарии |
| `create_task` | Новая задача: тип, приоритет, статус, исполнитель, родитель |
| `update_task` | Название, описание, статус (через workflow), тип, приоритет |
| `assign_task` | Назначить или снять исполнителя |
| `set_due_date` | Поставить или снять срок |
| `add_comment` | Комментарий в Markdown, `@username` — упоминание |
| `add_checklist_item`, `update_checklist_item` | Пункт чек-листа: добавить, отметить, переписать |
| `find_users` | Люди по части имени — для исполнителя |
| `search` | Гибридный поиск по задачам, комментариям, файлам, проектам и людям |

Модель называет вещи так, как их видит человек: проект — ключом (`PROJ`), задача — кодом (`PROJ-142`, в том числе
прежним после переноса), статус и тип — именем, человек — username, `me` или `none`. Guid тоже принимаются.
Ответы — с именами вместо Id и со ссылкой на карточку задачи. Справка по полям FQL уходит клиенту в инструкциях
сервера и собирается из того же каталога `FqlFields`, что у подсказок в интерфейсе.

Удаления, слияния, разделения и переноса между проектами здесь нет намеренно: это необратимые операции.

Ошибка Application (нет прав, нет статуса, отказ workflow, ошибка FQL) возвращается результатом инструмента
с `isError` и человеческим текстом — модель видит причину и может поправиться. Скрытый проект неотличим
от несуществующего, как 404 в API.

## Настройки

- `Auth:McpClient:ClientId` и `Auth:McpClient:RedirectUris` — клиент OIDC для MCP (по умолчанию — в `appsettings.json`);
  сеет его `ClientSeeder` при старте, правка подхватывается перезапуском.
- Адрес ресурса MCP — `{Auth:Issuer}/mcp`; он же разрешён клиенту `flow-mcp` как `resource` (RFC 8707). За Caddy
  `AUTH_ISSUER` уже совпадает с внешним адресом, отдельной настройки нет.

## Тесты

- `Flow.Api.Tests/McpApiTests` — настоящий клиент SDK поверх тестового хоста: 401 без токена, список инструментов,
  жизненный цикл задачи по именам, ошибки текстом.
- `Flow.Auth.Tests/McpOAuthTests` — каждый адрес возврата из таблицы выше принимается OpenIddict; путь клиента к токену целиком: `resource_metadata` в 401, метаданные ресурса
  и сервера авторизации, code + PKCE клиентом `flow-mcp` с `resource`, вызов `/mcp` с настоящей OpenIddict Validation.
