<picture>
  <source media="(prefers-color-scheme: dark)" srcset="src/Flow.Client/wwwroot/brand/logo-inverse.svg">
  <img src="src/Flow.Client/wwwroot/brand/logo.svg" alt="Flow" height="44">
</picture>

English · [Русский](README.ru.md)

A project task tracker built on .NET 10: a server API, a standalone authentication service, and a Blazor WebAssembly client. The whole stack starts with a single Docker command.

## What it is for

Flow tracks a team's work: projects (boards) with their own status sets, tasks with human-readable codes (`FLOW-12`), assignees, roles and access rules. Everything runs on your own infrastructure — no external services required.

The longer-term goal is an AI assistant inside the tracker: grounded in the team's knowledge base, it drafts specifications in the project's own format, decomposes them into board tasks, and builds business-process diagrams. Inference is planned to run locally so project data never leaves the team's infrastructure. Design work happens in [#1](https://github.com/azizmac/Flow/issues/1) and [#5](https://github.com/azizmac/Flow/issues/5); no AI subsystem exists in the code yet.

## What works today

- **Projects and tasks.** A board with a key (`^[A-Z][A-Z0-9]{1,9}$`), four default statuses, tasks coded `KEY-N`, assignment, filtering by assignee.
- **Users.** Profiles (name, contacts, external links), `Invited / Active / Deactivated` states, search and autocomplete.
- **Roles and permissions.** `Reader → Member → Developer → Admin → Owner`; the permission matrix is enforced on the server, and the client hides actions the current user cannot perform. A "last Owner" rule prevents locking the instance out of administration.
- **Task timeline.** Markdown comments with `@mentions` (a GitHub-style editor with preview and toolbar) and a change log: title, description, status, assignee, due date, deleted comments. Due dates with overdue highlighting.
- **Authentication.** A separate `Flow.Auth` service: ASP.NET Core Identity with BCrypt and OpenIddict (authorization code + PKCE, refresh, client credentials). The client signs in over OIDC; the API acts as a resource server validating Bearer JWTs. The initial password must be changed at first sign-in.
- **Attachments.** Files on a task: stored in S3-compatible storage, size and type limits, downloads only over an authorised request. API only for now — the UI and drag & drop come next.
- **Search.** Hybrid vector and full-text search over tasks, comments, projects and people on pgvector: it finds by meaning, not by substring. A sidebar box with live suggestions and a results page with filters; the index is updated in the same transaction as the edit.
- **Infrastructure.** PostgreSQL 16, EF Core, migrations applied on API startup. Build, tests and image publishing run in GitHub Actions.

![Tasks of a project](docs/images/board.png)

![Task timeline](docs/images/task-timeline.png)

## Roadmap

Done:

- [x] Boards, statuses, tasks, task codes
- [x] User profiles and external links
- [x] Roles, user states, permission matrix
- [x] Authentication extracted into an OpenIddict service, OIDC sign-in from the client
- [x] Mandatory initial password change
- [x] Whole stack in Docker with one command, CI building and publishing images
- [x] Comments, change log and Markdown editor on a task, due dates
- [x] Vector search: pgvector index, Qwen3 embedder, hybrid ranking with RRF and search in the UI (stages 1-5 of `docs/TZ_search_vector.md`)

Next — the `Flow.AI` subsystem ([#5](https://github.com/azizmac/Flow/issues/5), [#1](https://github.com/azizmac/Flow/issues/1)):

- [ ] **Phase A. Context storage design** — schema for memory, skills, sessions, knowledge base and artifacts; memory budgeting and consolidation; session history compression; pgvector on the existing PostgreSQL
- [ ] **Phase B. Integration spike** — pick an LLM and embedding provider, build a vertical slice of `POST /ai/chat` with memory and history, prototype skill trigger matching
- [ ] **Phase C. Specifications and diagrams** — a draft → decompose → critique → apply loop with versioned artifacts and task creation on a board; generating and reading draw.io diagrams, rendering them in the client

Every write performed by the AI goes through human confirmation: draft, confirm, then persist.

## Running it

Docker with the Compose plugin is the only prerequisite.

```bash
git clone https://github.com/azizmac/Flow.git
cd Flow
cp .env.example .env
sh docker/up.sh                                  # network and volumes, then data and application
```

What the script does step by step, and its flags, is covered in "The start script" below. On Windows, run `powershell -ExecutionPolicy Bypass -File docker/up.ps1` instead. The same thing by hand:

```bash
sh docker/data/init-env.sh                       # once: shared network and data volumes
docker compose -f docker-compose.data.yml up -d  # data: PostgreSQL and S3
docker compose up -d --build                     # application
```

The first build takes a few minutes. Once the containers are up, the client is at http://localhost:5016 — continue with "First sign-in" below.

Services: client :5016, API :8080, authentication :5100, PostgreSQL :5432, S3-compatible storage :9000 with its console on :9001. Ports and the bootstrap user's credentials come from `.env`, which is not tracked in git — the same file feeds both stacks. If a local PostgreSQL already holds port 5432, set `POSTGRES_PORT=5433`. On Windows, run `docker/data/init-env.ps1` instead of the shell script.

Token-signing certificates are generated on first start, and the `flow` and `flow_auth` databases are created by migrations. The two stacks start in any order: a service that comes up before the database waits for it (`Startup:DatabaseWaitTimeoutSeconds`, 60 s by default).

### The start script

`docker/up.sh` (and its Windows twin `docker/up.ps1`) is the same three commands in the right order, because Compose has no `depends_on` across projects and the data stack has to come up separately. Step by step:

1. **`.env`** — copied from `.env.example` if missing. Both stacks read it; without it the compose defaults apply.
2. **`docker/data/init-env.sh`** — the shared `flow-network` network and the external `flow-postgres-data`, `flow-minio-data` volumes. `DATA_ROOT` puts the volumes on a directory of your choice: `DATA_ROOT=/mnt/flow sh docker/up.sh` (only honoured when the volumes are created).
3. **Data stack** — `docker compose -f docker-compose.data.yml up -d`: PostgreSQL and S3.
4. **Application stack** — `docker compose up -d --build`: auth, api, client.

| Flag | What it does |
|---|---|
| *none* | brings everything up, rebuilding the application images |
| `--no-build` (`-NoBuild` in PowerShell) | skips the rebuild — a fast restart on unchanged code |
| `--help` | the short usage text from the script header |

Every step is idempotent: an existing network, volumes and `.env` are left alone, and running containers are not needlessly recreated. So the same `sh docker/up.sh` both sets the project up from scratch and updates it after a `git pull`.

You don't need the script if the data lives elsewhere: point `POSTGRES_HOST` and `S3_ENDPOINT` at it in `.env` and bring up the application alone — `docker compose up -d --build`. In Rider, the compound `Flow (full stack)` configuration starts both halves.

### Two stacks, and why

Data lives in its own Compose project (`docker-compose.data.yml`, project `flow-data`) on external volumes `flow-postgres-data` and `flow-minio-data`. The application stack owns no project data at all, so `docker compose down -v` cannot touch it — it only drops Flow.Auth's keys and certificates, which are recreated on the next start.

| What you want | Command |
|---|---|
| Update the application | `docker compose up -d --build` |
| Reset the application, keep the data | `docker compose down -v && docker compose up -d` |
| Stop the data stack (data stays) | `docker compose -f docker-compose.data.yml down` |
| Delete the data, deliberately | `docker compose -f docker-compose.data.yml down` then `docker volume rm flow-postgres-data flow-minio-data` |

To keep the data somewhere specific, create the volumes with `DATA_ROOT=/mnt/flow sh docker/data/init-env.sh`. To use a managed PostgreSQL or an external S3, point `POSTGRES_HOST` and `S3_ENDPOINT` at them in `.env` and skip `docker-compose.data.yml` entirely.

### Backups

```bash
# one-off backup into ./backups/<timestamp>
docker compose -f docker-compose.data.yml --profile backup run --rm backup /scripts/backup.sh

# scheduled: BACKUP_CRON (03:00 daily), keeping BACKUP_KEEP copies
docker compose -f docker-compose.data.yml --profile backup up -d

# restore; stop the application first, and `stop s3` for the objects
docker compose -f docker-compose.data.yml --profile backup run --rm backup \
    /scripts/restore.sh 2026-09-11T03-00-00 --yes
```

### Search: index and embeddings

Hybrid search over tasks, comments, projects and people (`docs/TZ_search_vector.md`): vectors plus full text,
merged with RRF. On by default; `SEARCH_ENABLED=false` restores the previous behaviour. Postgres runs the
`pgvector/pgvector:pg16` image (same data, same volumes); the `vector` extension is installed by a migration.

Turning it on needs an embedding model. It lives in the data stack under the `ai` profile — a `llama-server`
sidecar with `Qwen3-Embedding-0.6B`; weights go into the external `flow-models-data` volume (file name from
`EMBEDDINGS_MODEL_FILE`, volume location from `MODELS_ROOT`):

```bash
docker compose -f docker-compose.data.yml --profile ai up -d   # embedder on :8081
SEARCH_ENABLED=true docker compose up -d                        # API with indexing on
```

If the model runs on another machine (a GPU box, say), skip the `ai` profile and point
`EMBEDDINGS_QUERY_ENDPOINT` and `EMBEDDINGS_INDEXING_ENDPOINT` at it.

After that the index fills itself: every edit of a task, comment, project or person is queued in the same
transaction as the edit, and a background worker computes the vectors. Endpoints:

| Method | Path | Who | What for |
|---|---|---|---|
| `GET` | `/search?q=…` | any role | Hybrid search: vectors plus full text, merged with RRF, with highlighting |
| `GET` | `/search/status` | Admin, Owner | Queue size, stuck entries, chunk counts per type, model version, embedder availability |
| `POST` | `/search/reindex` | Owner | Queue everything (or one project / selected source types) — on first enable and after a model change |

Search parameters: `types` (`task,comment,board,user`), `boardId`, `includeArchived` (closed tasks are hidden by
default), `mode` (`hybrid`, `semantic`, `text` — for debugging relevance), `limit` and `offset`. With the model
unavailable the request still succeeds: the full-text half answers and the response is flagged `degraded: true`.

Part of the query is parsed without the model: `@ivanov` and `мои` ("mine") filter by assignee, `PROJ-142` jumps
straight to the task, plus `проект:DBACK` (project), `статус:в работе` (status), `просроченные` (overdue) and
`за неделю` (last week). Recognised filters are shown as chips and the rest of the line goes to normal search.
Alongside it, `GET /tasks/{id}/similar` returns similar tasks from the task's own vector, with no model call —
in the UI that is the "Похожие задачи" block on the task card and in the create form.

In the UI search is a box in the sidebar (suggestions as you type, grouped by type) and a `/search` page
with filters, match highlighting and pagination.

### Coming from a single-stack checkout

Volumes were renamed, so an existing deployment has to move its data over once:

```bash
docker compose down
sh docker/data/init-env.sh
docker run --rm -v flow_postgres-data:/from -v flow-postgres-data:/to alpine \
    sh -c 'cd /from && cp -a . /to'
docker run --rm -v flow_minio-data:/from -v flow-minio-data:/to alpine \
    sh -c 'cd /from && cp -a . /to'
docker compose -f docker-compose.data.yml up -d
docker compose up -d
```

Check `docker compose -f docker-compose.data.yml logs postgres` and sign in before removing the old volumes (`docker volume rm flow_postgres-data flow_minio-data`).

## First sign-in

The first start creates a bootstrap user — the single account every other account is created from:

| | |
|---|---|
| Login | `admin` (the email `admin@flow.com` works too) |
| Password | `admin` — must be changed at first sign-in |
| Role | Owner: full rights over projects, people and roles |

The values come from `.env` (`BOOTSTRAP_USERNAME`, `BOOTSTRAP_EMAIL`, `BOOTSTRAP_PASSWORD`) and are applied once, when the database is created. Changing them after the first run has no effect — change the password through the UI instead.

**1. Open http://localhost:5016.** The client checks for a session and, finding none, sends you to the Flow.Auth sign-in page on :5100. The login field accepts either a username or an email.

![Flow sign-in page](docs/images/login.png)

**2. Set your own password.** The initial password is flagged as temporary, so a password-change form opens instead of the app: the current password is `admin`, the new one must be at least 8 characters. Authentication does not complete until it is changed.

![Mandatory initial password change](docs/images/change-password.png)

**3. You are in.** After saving, the browser returns to the client with a session, on the Projects page. From there, "Новый проект" asks for a name and a key (say `WEB`), and tasks get codes `WEB-1`, `WEB-2` and so on.

![Project list](docs/images/boards.png)

**4. Check yourself under "Люди" (People).** The bootstrap user shows up as "Основатель" (Owner) with an active status; the "Добавить" button creates everyone else — they also get a temporary password to change at their first sign-in.

![People section showing the bootstrap user](docs/images/users.png)

Sign out with the icon next to your name at the bottom of the sidebar. A forgotten password is reset by an Owner from that person's profile; if the Owner account itself is lost, removing the data volumes (see "Two stacks, and why") wipes everything and the bootstrap user is created again.

The interface is in Russian.

## Documentation

Project documentation is written in Russian.

- [`AGENTS.md`](AGENTS.md) — architecture, invariants, conventions, commands
- [`docs/TZ_board_task_status.md`](docs/TZ_board_task_status.md) — boards, statuses, tasks
- [`docs/TZ_user.md`](docs/TZ_user.md) — users and profiles
- [`docs/TZ_user_roles.md`](docs/TZ_user_roles.md) — roles, states, permission matrix
- [`docs/TZ_auth.md`](docs/TZ_auth.md) — authentication and Flow.Auth
- [`docs/TZ_infra_data_split.md`](docs/TZ_infra_data_split.md) — splitting the database and S3 into a data stack
- [`docs/TZ_task_activity_comments.md`](docs/TZ_task_activity_comments.md) — comments, change log, Markdown editor, due dates
- [`docs/TZ_attachments.md`](docs/TZ_attachments.md) — attachments: storage, drag & drop, search by content
- [`docs/TZ_search_vector.md`](docs/TZ_search_vector.md) — vector and smart search: model, storage, indexing, stages
- [`docs/TZ_search_stage1-3.md`](docs/TZ_search_stage1-3.md) — index schema, embedder and indexing (stages 1–3)
- [`docs/Struktura_board_task_status.md`](docs/Struktura_board_task_status.md) — domain model structure
- [`docs/Sravnenie_DbContext_podhodov.md`](docs/Sravnenie_DbContext_podhodov.md) — DbContext approaches compared

## License

The source is open: anyone may use, study, modify and self-host Flow for their own purposes, free of charge. Selling Flow — as a product, a subscription, or a hosted service — is reserved to the copyright holders. A formal license text will be added separately.
