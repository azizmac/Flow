# CI/CD: выкатка на хост self-hosted раннера

Kubernetes убран. Сервер, на котором зарегистрирован self-hosted раннер (метка `flow`), сам и есть хост Flow:
выкатка — это те же два стека docker compose, что у разработчика (`docker-compose.data.yml` и
`docker-compose.yml`), только `.env` собирается из GitHub Variables/Secrets, а образ api берётся из реестра
по digest'у. Прежнее ТЗ — `docs/TZ_cicd_k8s.md`, оставлено как история.

## Пайплайн (`.github/workflows/ci.yml`)

```
проверки ─┐
сборка ───┼─ тесты ── образ flow-api (Docker Hub, digest) ── выкатка · хост ([self-hosted, flow])
          └────────────────────────────────────────────────────────────┘
```

- **Проверки и тесты** — на `ubuntu-latest`, как и раньше.
- **Образ** публикуется только с `main` и не из PR.
- **Выкатка** — push в `main` и ручной запуск с `main`, после зелёных проверок и тестов. Образ не
  пересобирался (api не менялся) — на сервере остаётся прежний, применяются только настройки и compose-файлы.
  Джоба в `environment: production` (можно повесить ручное подтверждение) и в группе `deploy-host`:
  две выкатки не идут одновременно.

Шаги выкатки:

1. В `DEPLOY_DIR` (по умолчанию `/opt/flow`) копируются compose-файлы и `docker/{ai,data}`, пишется `.env`
   (права 600). Рабочий каталог раннера не годится: checkout чистит его перед каждой джобой, а в
   `DEPLOY_DIR` живут `backups/` и bind-монтирования стека данных.
2. `docker/data/init-env.sh` — сеть `flow-network` и внешние тома (идемпотентно).
3. Ручной запуск с `pull_models` — веса моделей в `flow-models-data`.
4. Стек данных: `postgres` и `s3` до healthy, `s3-init` создаёт бакет (провал роняет выкатку), `backup`
   и `ai` — по `BACKUP_ENABLED` / `AI_ENABLED`; при правке `models.ini` сервис `ai` пересоздаётся.
5. `docker pull` образа api (логин во временный `DOCKER_CONFIG`, токен не остаётся на сервере).
6. Миграции — `docker compose run --rm api --migrate` новым образом **до** замены контейнера:
   упали — старый api продолжает работать.
7. `docker compose -p flow up -d --no-build api`, затем проверка, что контейнер на ожидаемом образе,
   и ожидание 200 от `http://127.0.0.1:$API_PORT/health/ready` (`DEPLOY_TIMEOUT`, 300 с).
8. Уборка своих неиспользуемых образов (по метке `org.opencontainers.image.source`), итог в Job Summary.

## Подготовка сервера (один раз)

1. Docker Engine с плагином compose, `curl`. Для `AI_BACKEND=cuda` — NVIDIA Container Toolkit.
2. Раннер: Settings → Actions → Runners → New self-hosted runner, при `config.sh` добавить метку `flow`
   (`--labels flow`), запустить как сервис (`svc.sh install && svc.sh start`).
3. Пользователь раннера — в группе `docker`, каталог установки его:
   `sudo mkdir -p /opt/flow && sudo chown <runner-user> /opt/flow`.
4. Порт `API_PORT` (8080) открыть наружу или поставить перед ним реверс-прокси с TLS
   (тогда `AUTH_ALLOW_INSECURE_HTTP=false`, `AUTH_ISSUER=https://…`).

## Variables и Secrets

Полный список с описаниями — `deploy/required-vars.txt`, его же проверяет джоба «Проверка · переменные».

| Обязательно | Что |
|---|---|
| Variables | `IMAGE_PREFIX`, `AUTH_ISSUER` (адрес, который видит браузер) |
| Secrets | `REGISTRY_USERNAME`, `REGISTRY_PASSWORD`, `POSTGRES_PASSWORD`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `AUTH_CERT_PASSWORD`, `BOOTSTRAP_PASSWORD` |

Всё остальное необязательно: пустая переменная в `.env` не пишется, действует дефолт compose. Внутренние
адреса (`postgres`, `http://s3:9000`, `http://ai:8081/v1`) задавать не нужно — оба стека на одном хосте
в общей сети. Значения пишутся в `.env` в одинарных кавычках, поэтому одинарной кавычки в них быть не должно.

## Поиск с моделями

1. Ручной запуск CI с `pull_models` (при `VISION_ENABLED=true` качаются и визуальные веса).
2. `AI_ENABLED=true`, при карте — `AI_BACKEND=cuda` (буферы 2048/512/512 подставятся сами).

## Откат

Итог выкатки печатает предыдущий образ. На сервере: вернуть его в `DEPLOY_DIR/.env`
(`FLOW_API_IMAGE='…'`) и `docker compose -p flow -f docker-compose.yml up -d --no-build api` — образ
дотянется из реестра (старые образы выкатка удаляет; для приватного репозитория сначала `docker login`).
Миграции назад не катятся — откат через границу миграции требует восстановления бэкапа
(`docker/data/restore.sh`).

## Что удалено

`k8s/**`, `.github/actions/{k8s-tools,kube-apply}`, `scripts/{check-manifests.py,resolve-images.py}`,
`deploy/runner-rbac.yaml`, мёртвые `build.yml`/`compose.yml`/`docker.yml`. Секреты `KUBE_DEPLOY_B64`,
`GH_RUNNERS_READ_TOKEN` и переменные `K8S_*`, `WORKER_NODE`, `GPU_NODE`, `STORAGE_CLASS`, `INGRESS_CLASS`,
`PUBLIC_*`, `AI_CPU_LIMIT`, `BACKUP_TZ`, `*_IMAGE` для postgres/MinIO больше не читаются — их можно удалить
из настроек репозитория.
