#!/bin/sh
# Сверяет ${VAR} из обоих compose-файлов со списком объявленных имён: deploy/required-vars.txt
# (что задаётся в GitHub и уезжает в кластер) и .env.example (что задаётся на машине разработчика).
#
#   sh scripts/check-compose-env.sh
#
# Ловит один конкретный класс ошибок: сервис читает переменную, а объявить её забыли нигде. Такая
# переменная не падает и не пустует — у неё в compose есть дефолт, — поэтому обнаруживается она обычно
# на чужой машине и через несколько недель. Ровно так когда-то потерялся AUTH_ISSUER.
#
# Падаем только на этом. Обратное направление (имя объявлено, а compose его не читает) печатается
# справкой: строка могла устареть, а могла обслуживать скрипты docker/data/*.sh — их мы тоже смотрим,
# прежде чем считать имя лишним. И отдельно: список из required-vars.txt заведомо шире compose —
# K8S_NAMESPACE или STORAGE_CLASS в compose взяться неоткуда, это не расхождение (§7.3 ТЗ).
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
REQUIRED=$ROOT/deploy/required-vars.txt
ENV_EXAMPLE=$ROOT/.env.example
SCRIPTS_DIR=$ROOT/docker/data

# Все compose-файлы проекта: основной стек, стек данных и оверлеи железа. Именно список, а не пара
# переменных: оверлеи профилей железа будут добавляться, и каждый забытый — это дырка в проверке,
# через которую уезжает необъявленная переменная.
set -- docker-compose.yml docker-compose.data.yml docker/ai/nvidia.yml

for file in "$@" deploy/required-vars.txt .env.example; do
    if [ ! -f "$ROOT/$file" ]; then
        echo "не найден файл: $ROOT/$file" >&2
        exit 2
    fi
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT HUP INT TERM

# Имена вида ${VAR} и ${VAR:-default}. Сначала убираем экранированные доллары ($$): в compose это
# способ отдать литеральный $ внутрь контейнера (entrypoint у s3-init), а не обращение к переменной.
compose_vars() {
    sed 's/\$\$//g' "$1" \
        | grep -o '\${[A-Za-z_][A-Za-z0-9_]*' \
        | sed 's/^\${//' \
        | sort -u
}

# По файлу на список: имя файла нужно потом в отчёте, чтобы «переменная не объявлена» сразу говорило,
# где её искать. Индекс в имени временного файла — потому что в имени самого compose-файла есть слеши.
index=0
: > "$work/used"
for rel in "$@"; do
    index=$((index + 1))
    compose_vars "$ROOT/$rel" > "$work/vars-$index"
    echo "$rel" > "$work/name-$index"
    cat "$work/vars-$index" >> "$work/used"
done
files_count=$index
sort -u -o "$work/used" "$work/used"

# Объявленные имена: первое поле required-vars.txt и левая часть присваиваний .env.example.
cut -d'|' -f1 "$REQUIRED" | grep -E '^[A-Z][A-Z0-9_]*$' | sort -u > "$work/required"
sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$ENV_EXAMPLE" | sort -u > "$work/example"
sort -u "$work/required" "$work/example" > "$work/declared"

# Кто ещё читает переменные .env, кроме compose: скрипты стека данных (init-env.sh создаёт тома по
# DATA_ROOT и MODELS_ROOT, backup.sh и scheduler.sh живут на своих). Без этого они выглядели бы лишними.
: > "$work/scripts"
if [ -d "$SCRIPTS_DIR" ]; then
    for file in "$SCRIPTS_DIR"/*.sh; do
        [ -f "$file" ] || continue
        grep -oE '\$\{?[A-Za-z_][A-Za-z0-9_]*' "$file" | tr -d '${' >> "$work/scripts"
    done
fi
sort -u -o "$work/scripts" "$work/scripts"

printf 'compose читает переменных: %s; объявлено имён: %s (%s в required-vars.txt, %s в .env.example)\n' \
    "$(wc -l < "$work/used" | tr -d ' ')" \
    "$(wc -l < "$work/declared" | tr -d ' ')" \
    "$(wc -l < "$work/required" | tr -d ' ')" \
    "$(wc -l < "$work/example" | tr -d ' ')"

# Направление 1 — ошибка: читается сервисом, но не объявлена нигде.
comm -23 "$work/used" "$work/declared" > "$work/undeclared"

# Направление 2 — справка: объявлено в .env.example, но не читает ни compose, ни docker/data/*.sh.
sort -u "$work/used" "$work/scripts" > "$work/consumers"
comm -23 "$work/example" "$work/consumers" > "$work/unused"

if [ -s "$work/unused" ]; then
    printf '\nОбъявлены в .env.example, но их никто не читает (не ошибка, но строка, похоже, устарела):\n'
    while IFS= read -r name; do
        printf '  %s\n' "$name"
    done < "$work/unused"
fi

if [ -s "$work/undeclared" ]; then
    printf '\nЧитаются в compose, но не объявлены ни в .env.example, ни в deploy/required-vars.txt:\n' >&2
    while IFS= read -r name; do
        where=
        index=0
        while [ "$index" -lt "$files_count" ]; do
            index=$((index + 1))
            if grep -qx "$name" "$work/vars-$index"; then
                if [ -n "$where" ]; then
                    where="$where и $(cat "$work/name-$index")"
                else
                    where=$(cat "$work/name-$index")
                fi
            fi
        done
        printf '  %s — читается в %s\n' "$name" "$where" >&2
        if [ -n "${GITHUB_ACTIONS:-}" ]; then
            printf '::error title=Переменные compose::%s читается в %s, но нигде не объявлена\n' "$name" "$where"
        fi
    done < "$work/undeclared"
    printf '\nКаждую нужно либо добавить в .env.example (её задают на машине), либо в deploy/required-vars.txt\n' >&2
    printf 'с этапом и описанием (её задают в GitHub и она уезжает в кластер). Дефолт в compose не считается\n' >&2
    printf 'объявлением: именно он и прячет забытую переменную до первого чужого запуска.\n' >&2
    exit 1
fi

printf '\nвсе переменные compose объявлены\n'
