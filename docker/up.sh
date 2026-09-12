#!/bin/sh
# Поднимает оба стека одной командой: сеть и тома → данные → приложение. Compose не умеет
# depends_on между проектами, поэтому порядок задаёт скрипт. Порядок при этом не критичен:
# сервис, стартовавший раньше базы, дожидается её сам (Startup:DatabaseWaitTimeoutSeconds).
# Все шаги идемпотентны — скрипт можно запускать и на первом старте, и на обновлении.
# Windows-аналог — docker/up.ps1. См. docs/TZ_infra_data_split.md.
#
#   sh docker/up.sh                        # поднять всё, образы приложения пересобрать
#   sh docker/up.sh --no-build             # без пересборки
#   DATA_ROOT=/mnt/flow sh docker/up.sh    # при первом запуске: данные на своём каталоге
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD=--build

for arg in "$@"; do
    case $arg in
        --no-build)
            BUILD=
            ;;
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "неизвестный аргумент: $arg (см. --help)" >&2
            exit 2
            ;;
    esac
done

cd "$ROOT"

# Переменные читают оба стека; без файла берутся значения по умолчанию из compose.
if [ ! -f .env ]; then
    cp .env.example .env
    echo ".env создан из .env.example — проверьте порты и пароли"
    echo
fi

sh docker/data/init-env.sh

echo
echo "== данные =="
docker compose -f docker-compose.data.yml up -d

echo
echo "== приложение =="
docker compose up -d $BUILD

# .env читает Compose, а не этот скрипт — порт клиента достаём оттуда только ради сообщения.
CLIENT_PORT=$(sed -n 's/^CLIENT_PORT=//p' .env 2>/dev/null | tail -1)

echo
echo "Готово. Клиент: http://localhost:${CLIENT_PORT:-5016}"
echo "Состояние:  docker compose ps && docker compose -f docker-compose.data.yml ps"
