#!/bin/sh
# Точка входа сервиса backup: расписание из BACKUP_CRON, задача — backup.sh.
# Отдельный файл нужен потому, что busybox crond запускает задачи с пустым окружением:
# переменные контейнера сохраняются в файл и подключаются в самой задаче.
set -eu

CRON=${BACKUP_CRON:-0 3 * * *}

export -p > /tmp/backup.env
mkdir -p /etc/crontabs
# Вывод задачи уходит в stdout первого процесса — то есть в docker compose logs backup.
echo "$CRON . /tmp/backup.env; /scripts/backup.sh >> /proc/1/fd/1 2>&1" > /etc/crontabs/root

echo "бэкапы по расписанию: $CRON (храним ${BACKUP_KEEP:-7} последних)"
exec crond -f -l 8
