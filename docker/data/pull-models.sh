#!/bin/sh
# Качает веса моделей поиска в том flow-models-data. Запускается после docker/data/init-env.sh
# и до профиля ai: без файлов llama-server не стартует и контейнер уходит в перезапуск.
#
#   sh docker/data/pull-models.sh                # эмбеддер и реранкер
#   sh docker/data/pull-models.sh embeddings     # только эмбеддер (машина без видеокарты)
#   sh docker/data/pull-models.sh vision         # визуальная модель и её проектор
#   sh docker/data/pull-models.sh all+vision     # все три модели
#   sh docker/data/pull-models.sh --force        # перекачать, затерев скачанное
#
# Визуальная модель в набор «all» не входит намеренно: это полтора гигабайта сверху, и нужны они
# только при VISION_ENABLED=true. Отдельного сервиса у неё больше нет — она третья секция того же
# пресета llama-server, поэтому и веса лежат рядом с остальными, а не в кэше HuggingFace.
#
# Качает контейнером, а не хостовым curl: на новой машине из инструментов гарантирован только Docker,
# и тот же способ работает одинаково в Linux, WSL и Git Bash. Имена файлов должны совпадать с
# EMBEDDINGS_MODEL_FILE, RERANKER_MODEL_FILE, VISION_MODEL_FILE и VISION_MMPROJ_FILE из .env —
# меняете файл, задайте и ссылку (…_URL).
set -eu

VOLUME=${MODELS_VOLUME:-flow-models-data}
# Образ без лишнего: busybox wget умеет https и пишет прогресс, а запускается он root'ом и потому
# может писать в том (у alpine/curl пользователь без прав на /models).
IMAGE=${PULL_IMAGE:-alpine}

EMBEDDINGS_MODEL_FILE=${EMBEDDINGS_MODEL_FILE:-Qwen3-Embedding-0.6B-Q8_0.gguf}
EMBEDDINGS_MODEL_URL=${EMBEDDINGS_MODEL_URL:-https://huggingface.co/Qwen/Qwen3-Embedding-0.6B-GGUF/resolve/main/Qwen3-Embedding-0.6B-Q8_0.gguf}

# Не Qwen3-Reranker из ТЗ: тот causal LM без классификационной головы, и llama.cpp ранжирует им мусором.
# Нужен cross-encoder с головой — bge-reranker-v2-m3, многоязычный и того же размера.
RERANKER_MODEL_FILE=${RERANKER_MODEL_FILE:-bge-reranker-v2-m3-Q8_0.gguf}
RERANKER_MODEL_URL=${RERANKER_MODEL_URL:-https://huggingface.co/gpustack/bge-reranker-v2-m3-GGUF/resolve/main/bge-reranker-v2-m3-Q8_0.gguf}

# Визуальная модель и отдельно веса её проектора: mmproj — это та часть, что превращает пиксели
# в токены, и без неё модель принимает только текст. Квантизация Q4_K_M выбрана по замеру на карте
# с 4 ГБ: с ней рядом помещаются обе текстовые модели, с Q8_0 (1.8 ГБ вместо 1.1) — уже нет.
VISION_MODEL_FILE=${VISION_MODEL_FILE:-Qwen3-VL-Embedding-2B.Q4_K_M.gguf}
VISION_MODEL_URL=${VISION_MODEL_URL:-https://huggingface.co/mradermacher/Qwen3-VL-Embedding-2B-GGUF/resolve/main/Qwen3-VL-Embedding-2B.Q4_K_M.gguf}
VISION_MMPROJ_FILE=${VISION_MMPROJ_FILE:-Qwen3-VL-Embedding-2B.mmproj-Q8_0.gguf}
VISION_MMPROJ_URL=${VISION_MMPROJ_URL:-https://huggingface.co/mradermacher/Qwen3-VL-Embedding-2B-GGUF/resolve/main/Qwen3-VL-Embedding-2B.mmproj-Q8_0.gguf}

what=all
force=0

for argument in "$@"; do
    case "$argument" in
        all|all+vision|embeddings|reranker|vision) what=$argument ;;
        --force|-f) force=1 ;;
        -h|--help)
            sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "неизвестный аргумент: $argument (ожидается all | embeddings | reranker | --force)" >&2
            exit 2
            ;;
    esac
done

if ! docker volume inspect "$VOLUME" >/dev/null 2>&1; then
    echo "тома $VOLUME нет — сначала: sh docker/data/init-env.sh" >&2
    exit 1
fi

# Скачивает во временный файл и переименовывает только целиком: оборванная закачка не должна
# выглядеть готовой моделью — llama-server принял бы её за битый файл и падал бы на старте.
download() {
    file=$1
    url=$2

    if [ "$force" -eq 0 ] && in_volume "test -s '/models/$file'"; then
        echo "$file уже есть ($(size_of "$file")) — пропускаем"
        return
    fi

    echo "качаем $file"
    in_volume "wget -O '/models/$file.part' '$url' && mv '/models/$file.part' '/models/$file'"

    echo "$file готов ($(size_of "$file"))"
}

# Всё, что трогает /models, уходит одной строкой в sh -c. Отдельным аргументом Git Bash подменил бы
# /models на свой корень (C:/Program Files/Git/models): проверка «файл уже есть» молча не срабатывала
# бы, и скрипт качал бы гигабайты заново — так и случилось при первом прогоне.
in_volume() {
    docker run --rm -v "$VOLUME:/models" "$IMAGE" sh -c "$1"
}

size_of() {
    in_volume "du -h '/models/$1' | cut -f1"
}

case "$what" in
    all|all+vision)
        download "$EMBEDDINGS_MODEL_FILE" "$EMBEDDINGS_MODEL_URL"
        download "$RERANKER_MODEL_FILE" "$RERANKER_MODEL_URL"
        ;;
    embeddings) download "$EMBEDDINGS_MODEL_FILE" "$EMBEDDINGS_MODEL_URL" ;;
    reranker) download "$RERANKER_MODEL_FILE" "$RERANKER_MODEL_URL" ;;
esac

case "$what" in
    vision|all+vision)
        download "$VISION_MODEL_FILE" "$VISION_MODEL_URL"
        download "$VISION_MMPROJ_FILE" "$VISION_MMPROJ_URL"
        ;;
esac

echo
echo "Готово. Дальше:"
echo "  docker compose -f docker-compose.data.yml --profile ai up -d"
