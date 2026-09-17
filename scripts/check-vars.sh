#!/bin/sh
# Проверяет, что переменные и секреты, без которых выкатка этапа не имеет смысла, действительно заданы.
# Источник истины — deploy/required-vars.txt (он же источник таблиц §7 docs/TZ_cicd_k8s.md).
#
#   sh scripts/check-vars.sh              # этап 1: публикация образов
#   sh scripts/check-vars.sh 3            # плюс всё, что нужно слою данных
#   sh scripts/check-vars.sh 1 --no-secrets   # только переменные, без секретов
#
# Проверка существует потому, что забытая переменная сама по себе не падает: ASP.NET подставляет
# дефолт из appsettings.json, под стартует зелёным, rollout рапортует успех — и прод уезжает с паролем
# «flow» и localhost-адресами. Поймать это дешевле всего до того, как что-то соберётся.
#
# В CI ни vars, ни secrets в шаг сами не попадают: джоба обязана перечислить нужные в env, иначе
# скрипт увидит пустое окружение и честно скажет, что не задано ничего. Локально то же самое делает
# export (или запуск вида `REGISTRY=ghcr.io IMAGE_PREFIX=azizmac sh scripts/check-vars.sh 1`).
#
# Значений не печатает вообще ни у одной строки: секрет отличается от обычной переменной только
# пометкой в файле, а цена ошибки несимметрична — в отчёт идут имена и «задана / не задана».
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
LIST=${REQUIRED_VARS_FILE:-$ROOT/deploy/required-vars.txt}
STAGE=1
CHECK_SECRETS=1

# --no-secrets нужен для прогонов, где секретов физически нет и это нормально: в pull request из форка
# GitHub их не выдаёт вовсе. Проверять там нечего, а красный крестик на чужом PR ничего не значит,
# кроме того, что автор PR — не владелец репозитория.
for argument in "$@"; do
    case "$argument" in
        --no-secrets) CHECK_SECRETS=0 ;;
        *) STAGE=$argument ;;
    esac
done

# Возврат кода 2 (а не 1) на ошибку самого скрипта: в логе джобы сразу видно, что упала проверка,
# а не проверяемое.
case "$STAGE" in
    [1-6]) ;;
    *)
        echo "этап должен быть числом от 1 до 6, получено «$STAGE»" >&2
        exit 2
        ;;
esac

if [ ! -f "$LIST" ]; then
    echo "не найден список переменных: $LIST" >&2
    exit 2
fi

# Косвенного разыменования («значение переменной с таким именем») в POSIX sh нет, поэтому eval.
# Имена приходят из нашего же файла и ниже проверяются шаблоном, так что снаружи сюда ничего не влезет.
value_of() {
    eval "printf '%s' \"\${$1:-}\""
}

CR=$(printf '\r')

missing=0
checked=0
skipped=0
rows=
errors=

# Файл на Windows-чекауте может быть с CRLF, и тогда «пустая» строка состоит из одного : для sh она не
# пустая, и разбор спотыкается на ней с сообщением про недопустимое имя. Поэтому читаем не сам файл,
# а его копию без возвратов каретки. Копия временная и убирается вместе с каталогом.
rows_source="$(mktemp)"
trap 'rm -f "$rows_source"' EXIT INT TERM
tr -d "$CR" < "$LIST" > "$rows_source"

# Читаем перенаправлением, а не через pipe: в POSIX sh каждый элемент конвейера — подоболочка,
# и счётчики, увеличенные внутри цикла, снаружи были бы потеряны.
while IFS='|' read -r name kind stage description || [ -n "$name" ]; do
    # Комментарии и пустые строки. Строка без разделителей целиком попадает в $name.
    case "$name" in
        ''|'#'*) continue ;;
    esac

    # Windows-чекаут может принести CRLF: возврат каретки достаётся последнему полю.
    description=${description%"$CR"}

    case "$name" in
        *[!A-Z0-9_]*)
            echo "$LIST: недопустимое имя «$name» — ожидается ИМЯ|тип|этап|описание" >&2
            exit 2
            ;;
    esac

    case "$kind" in
        var|secret) ;;
        *)
            echo "$LIST: у $name тип «$kind», а бывает только var или secret" >&2
            exit 2
            ;;
    esac

    case "$stage" in
        # Прочерк — переменная не обязательна никогда: либо есть разумный дефолт, либо законным
        # значением является пустое. Такие в отчёт не идут, только в счётчик.
        -)
            skipped=$((skipped + 1))
            continue
            ;;
        [1-6]) ;;
        *)
            echo "$LIST: у $name этап «$stage», а бывает 1-6 либо -" >&2
            exit 2
            ;;
    esac

    if [ "$stage" -gt "$STAGE" ]; then
        skipped=$((skipped + 1))
        continue
    fi

    # Секреты при --no-secrets не проверяем вовсе (см. комментарий к флагу): их отсутствие в форк-PR
    # ожидаемо, и падать на этом — значит приучить себя к красным крестикам без причины.
    if [ "$CHECK_SECRETS" -eq 0 ] && [ "$kind" = secret ]; then
        skipped=$((skipped + 1))
        continue
    fi

    checked=$((checked + 1))

    if [ -n "$(value_of "$name")" ]; then
        rows="$rows| \`$name\` | $kind | $stage | задана |
"
    else
        rows="$rows| \`$name\` | $kind | $stage | **не задана** |
"
        errors="$errors не задана переменная $name (нужна с этапа $stage): $description
"
        missing=$((missing + 1))
    fi
done < "$rows_source"

if [ "$checked" -eq 0 ]; then
    echo "$LIST: для этапа $STAGE не нашлось ни одной строки — список пуст или испорчен" >&2
    exit 2
fi

# Отчёт идёт в Job Summary, если он есть, и на экран, если скрипт запущен руками. Формат один и тот же:
# markdown-таблица читается и в терминале. Дописываем (>>), а не перезаписываем: файл Job Summary общий
# на всю джобу, и `>` стёр бы то, что туда положили соседние шаги. Через cat, а не через перенаправление
# в ${GITHUB_STEP_SUMMARY:-/dev/stdout}: в Git Bash на Windows «>> /dev/stdout» молча глотает вывод,
# и локальный запуск остался бы без отчёта.
report() {
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
        cat >> "$GITHUB_STEP_SUMMARY"
    else
        cat
    fi
}

{
    printf '### Проверка переменных · этап %s\n\n' "$STAGE"
    printf '| Переменная | Тип | Этап | Состояние |\n'
    printf '|------------|-----|------|-----------|\n'
    printf '%s' "$rows"
    printf '\nПроверено %s из %s строк `deploy/required-vars.txt`; ' "$checked" "$((checked + skipped))"
    printf 'остальные нужны на более поздних этапах либо не обязательны никогда.\n'
} | report

if [ "$missing" -gt 0 ]; then
    printf 'этап %s: не хватает %s из %s обязательных переменных\n' "$STAGE" "$missing" "$checked" >&2
    # В CI то же самое печатается аннотациями: причина видна в списке джоб, а не только в раскрытом
    # логе. Дублировать их ещё и обычным списком незачем — аннотации попадают в лог как есть.
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
        printf '%s' "$errors" | while IFS= read -r line; do
            printf '::error title=Переменные::%s\n' "${line# }"
        done
    else
        printf '%s' "$errors" >&2
    fi
    printf 'Задать их нужно в Settings → Secrets and variables → Actions, а джоба обязана перечислить их в env.\n' >&2
    exit 1
fi

printf 'этап %s: все обязательные переменные заданы (%s)\n' "$STAGE" "$checked"
