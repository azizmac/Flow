#!/bin/sh
# Создаёт общую сеть и внешние тома данных. Запускается один раз перед первым стартом, повторный
# запуск ничего не ломает. См. docs/TZ_infra_data_split.md.
#
#   sh docker/data/init-env.sh
#   DATA_ROOT=/mnt/flow sh docker/data/init-env.sh   # данные на своём каталоге вместо томов Docker
#   MODELS_ROOT=/mnt/models sh docker/data/init-env.sh  # веса моделей отдельно от данных
set -eu

NETWORK=flow-network
PG_VOLUME=flow-postgres-data
S3_VOLUME=flow-minio-data
MODELS_VOLUME=flow-models-data
DATA_ROOT=${DATA_ROOT:-}

if docker network inspect "$NETWORK" >/dev/null 2>&1; then
    echo "сеть $NETWORK уже есть"
else
    docker network create "$NETWORK" >/dev/null
    echo "сеть $NETWORK создана"
fi

# Том с bind-драйвером: данные лежат по указанному пути, а не в каталоге Docker.
# Пустой path — обычный том Docker.
create_volume() {
    name=$1
    path=$2

    if docker volume inspect "$name" >/dev/null 2>&1; then
        echo "том $name уже есть"
        return
    fi

    if [ -n "$path" ]; then
        mkdir -p "$path"
        docker volume create \
            --driver local \
            --opt type=none \
            --opt o=bind \
            --opt device="$path" \
            "$name" >/dev/null
        echo "том $name создан на $path"
    else
        docker volume create "$name" >/dev/null
        echo "том $name создан"
    fi
}

create_volume "$PG_VOLUME" "${DATA_ROOT:+$DATA_ROOT/postgres}"
create_volume "$S3_VOLUME" "${DATA_ROOT:+$DATA_ROOT/minio}"
# Веса моделей эмбеддера (профиль ai). MODELS_ROOT задаёт свой каталог — веса обычно живут не там,
# где данные: их не бэкапят и при желании переиспользуют между установками.
create_volume "$MODELS_VOLUME" "${MODELS_ROOT:-${DATA_ROOT:+$DATA_ROOT/models}}"

echo
echo "Готово. Дальше:"
echo "  docker compose -f docker-compose.data.yml up -d"
echo "  sh docker/data/pull-models.sh                      # веса моделей поиска (профиль ai)"
echo "  docker compose up -d --build"
