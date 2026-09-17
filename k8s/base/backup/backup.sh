#!/bin/sh
# Бэкап данных Flow: дампы обеих баз и объекты S3. Работает внутри сервиса backup стека данных —
# и по расписанию (scheduler.sh), и разово:
#
#   docker compose -f docker-compose.data.yml --profile backup run --rm backup /scripts/backup.sh
#
# Результат — /backups/<дата>/ (на хосте это BACKUP_DIR, по умолчанию ./backups).
# Старые копии чистятся, остаётся BACKUP_KEEP последних.
set -eu

DB=${POSTGRES_DB:-flow}
AUTH_DB=${AUTH_DB:-flow_auth}
KEEP=${BACKUP_KEEP:-7}
STAMP=$(date +%Y-%m-%dT%H-%M-%S)
DEST=/backups/$STAMP

mkdir -p "$DEST"
echo "[$(date +%H:%M:%S)] бэкап в $DEST"

pg_dump --format=custom --dbname="$DB" --file="$DEST/$DB.dump"
echo "  база $DB — $(du -h "$DEST/$DB.dump" | cut -f1)"

pg_dump --format=custom --dbname="$AUTH_DB" --file="$DEST/$AUTH_DB.dump"
echo "  база $AUTH_DB — $(du -h "$DEST/$AUTH_DB.dump" | cut -f1)"

# Объекты копируются с диска. Для строгой консистентности хранилище лучше остановить
# (docker compose -f docker-compose.data.yml stop s3), но для дев-стенда снимка на ходу достаточно.
if [ -d /minio-data ]; then
    tar czf "$DEST/minio-data.tar.gz" -C /minio-data .
    echo "  объекты S3 — $(du -h "$DEST/minio-data.tar.gz" | cut -f1)"
fi

# Ротация: имена каталогов сортируются как даты, лишние с начала списка. Только каталоги —
# посторонние файлы в BACKUP_DIR скрипт не трогает.
total=$(ls -1d /backups/*/ 2>/dev/null | wc -l)
if [ "$total" -gt "$KEEP" ]; then
    ls -1d /backups/*/ | sort | head -n "$((total - KEEP))" | while read -r old; do
        rm -rf "$old"
        echo "  удалён старый бэкап $(basename "$old")"
    done
fi

echo "[$(date +%H:%M:%S)] готово"
