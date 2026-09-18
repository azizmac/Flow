# Качает веса моделей поиска в том flow-models-data (Windows). Аналог docker/data/pull-models.sh.
#
#   powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1
#   powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1 -What embeddings
#   powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1 -What vision
#   powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1 -What all+vision
#   powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1 -Force
#
# Визуальная модель в набор all не входит: это полтора гигабайта сверху, и нужны они только при
# VISION_ENABLED=true. Файлов у неё два — веса и отдельно проектор (mmproj), без которого модель
# принимает только текст. Имена файлов должны совпадать с EMBEDDINGS_MODEL_FILE, RERANKER_MODEL_FILE,
# VISION_MODEL_FILE и VISION_MMPROJ_FILE из .env.
param(
    [ValidateSet("all", "all+vision", "embeddings", "reranker", "vision")]
    [string]$What = "all",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$volume = if ($env:MODELS_VOLUME) { $env:MODELS_VOLUME } else { "flow-models-data" }
# busybox из alpine умеет https и работает root'ом: у alpine/curl пользователь без прав на /models.
$image = if ($env:PULL_IMAGE) { $env:PULL_IMAGE } else { "alpine" }

$models = @{
    embeddings = @{
        File = if ($env:EMBEDDINGS_MODEL_FILE) { $env:EMBEDDINGS_MODEL_FILE } else { "Qwen3-Embedding-0.6B-Q8_0.gguf" }
        Url  = if ($env:EMBEDDINGS_MODEL_URL) { $env:EMBEDDINGS_MODEL_URL } else { "https://huggingface.co/Qwen/Qwen3-Embedding-0.6B-GGUF/resolve/main/Qwen3-Embedding-0.6B-Q8_0.gguf" }
    }
    # Не Qwen3-Reranker из ТЗ: тот causal LM без классификационной головы и ранжирует мусором.
    reranker   = @{
        File = if ($env:RERANKER_MODEL_FILE) { $env:RERANKER_MODEL_FILE } else { "bge-reranker-v2-m3-Q8_0.gguf" }
        Url  = if ($env:RERANKER_MODEL_URL) { $env:RERANKER_MODEL_URL } else { "https://huggingface.co/gpustack/bge-reranker-v2-m3-GGUF/resolve/main/bge-reranker-v2-m3-Q8_0.gguf" }
    }
    # Квантизация Q4_K_M по замеру на карте с 4 ГБ: рядом с ней помещаются обе текстовые модели.
    vision     = @{
        File = if ($env:VISION_MODEL_FILE) { $env:VISION_MODEL_FILE } else { "Qwen3-VL-Embedding-2B.Q4_K_M.gguf" }
        Url  = if ($env:VISION_MODEL_URL) { $env:VISION_MODEL_URL } else { "https://huggingface.co/mradermacher/Qwen3-VL-Embedding-2B-GGUF/resolve/main/Qwen3-VL-Embedding-2B.Q4_K_M.gguf" }
    }
    # Проектор: та часть модели, что превращает пиксели в токены. Отдельным файлом, отдельной строкой.
    mmproj     = @{
        File = if ($env:VISION_MMPROJ_FILE) { $env:VISION_MMPROJ_FILE } else { "Qwen3-VL-Embedding-2B.mmproj-Q8_0.gguf" }
        Url  = if ($env:VISION_MMPROJ_URL) { $env:VISION_MMPROJ_URL } else { "https://huggingface.co/mradermacher/Qwen3-VL-Embedding-2B-GGUF/resolve/main/Qwen3-VL-Embedding-2B.mmproj-Q8_0.gguf" }
    }
}

# Не `docker volume inspect`: на отсутствующем томе он пишет в stderr, а Windows PowerShell 5.1
# превращает stderr нативной команды в ошибку и под $ErrorActionPreference = "Stop" обрывает скрипт
# раньше, чем до объяснения дойдёт очередь. `volume ls` с фильтром просто ничего не печатает.
if (-not (docker volume ls --quiet --filter "name=^$volume$")) {
    Write-Error "тома $volume нет — сначала: powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1"
}

# Всё, что трогает /models, уходит одной строкой в sh -c — как в pull-models.sh: там отдельный
# аргумент /models подменяется Git Bash на путь установки Git, и обе версии должны вести себя одинаково.
function Invoke-InVolume($command) {
    docker run --rm -v "${volume}:/models" $image sh -c $command
}

function Get-Size($file) {
    Invoke-InVolume "du -h '/models/$file' | cut -f1"
}

# Скачивает во временный файл и переименовывает только целиком: оборванная закачка не должна
# выглядеть готовой моделью — llama-server принял бы её за битый файл и падал бы на старте.
function Get-Model($file, $url) {
    if (-not $Force) {
        Invoke-InVolume "test -s '/models/$file'" *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "$file уже есть ($(Get-Size $file)) — пропускаем"
            return
        }
    }

    Write-Host "качаем $file"
    Invoke-InVolume "wget -O '/models/$file.part' '$url' && mv '/models/$file.part' '/models/$file'"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "не удалось скачать $file"
    }

    Write-Host "$file готов ($(Get-Size $file))"
}

foreach ($name in @("embeddings", "reranker")) {
    if ($What -eq "all" -or $What -eq "all+vision" -or $What -eq $name) {
        Get-Model $models[$name].File $models[$name].Url
    }
}

if ($What -eq "vision" -or $What -eq "all+vision") {
    foreach ($name in @("vision", "mmproj")) {
        Get-Model $models[$name].File $models[$name].Url
    }
}

Write-Host ""
Write-Host "Готово. Дальше:"
Write-Host "  docker compose -f docker-compose.data.yml --profile ai up -d"
