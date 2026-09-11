#!/bin/sh
# Восстановление из бэкапа. Перезаписывает существующие данные, поэтому требует --yes.
#
#   docker compose -f docker-compose.data.yml --profile backup run --rm backup \
#       /scripts/restore.sh 2026-09-11T03-00-00 --yes
#
# Приложение на время восстановления лучше остановить: docker compose down.
# Объекты S3 восстанавливаются только при остановленном хранилище:
#   docker compose -f docker-compose.data.yml stop s3
set -eu

NAME=${1:-}
CONFIRM=${2:-}
DB=${POSTGRES_DB:-flow}
AUTH_DB=${AUTH_DB:-flow_auth}

if [ -z "$NAME" ]; then
    echo "укажите каталог бэкапа. Доступные:"
    ls -1 /backups 2>/dev/null || echo "  (в /backups пусто)"
    exit 1
fi

SRC=/backups/$NAME
[ -d "$SRC" ] || { echo "нет каталога $SRC"; exit 1; }

if [ "$CONFIRM" != "--yes" ]; then
    echo "восстановление из $SRC перезапишет текущие данные баз $DB и $AUTH_DB."
    echo "повторите с флагом --yes, если это то, что нужно."
    exit 1
fi

restore_db() {
    name=$1
    file=$SRC/$name.dump

    if [ ! -f "$file" ]; then
        echo "  $name — дампа нет, пропускаю"
        return
    fi

    # --clean --if-exists: объекты сносятся перед накатом, база остаётся той же.
    pg_restore --clean --if-exists --no-owner --dbname="$name" "$file"
    echo "  база $name восстановлена"
}

echo "восстановление из $SRC"
restore_db "$DB"
restore_db "$AUTH_DB"

if [ -f "$SRC/minio-data.tar.gz" ] && [ -d /minio-data ]; then
    rm -rf /minio-data/..?* /minio-data/.[!.]* /minio-data/* 2>/dev/null || true
    tar xzf "$SRC/minio-data.tar.gz" -C /minio-data
    echo "  объекты S3 восстановлены (хранилище должно быть остановлено, иначе поднимите его заново)"
fi

echo "готово"
