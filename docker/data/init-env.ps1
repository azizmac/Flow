# Создаёт общую сеть и внешние тома данных (Windows). Аналог docker/data/init-env.sh.
#
#   powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1
#   $env:DATA_ROOT="D:\flow"; powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1
#   $env:MODELS_ROOT="D:\models"; powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1
$ErrorActionPreference = "Stop"

$network = "flow-network"
$volumes = @{ "flow-postgres-data" = "postgres"; "flow-minio-data" = "minio"; "flow-models-data" = "models" }
$dataRoot = $env:DATA_ROOT
# Веса моделей поиска (профиль ai) обычно живут не там, где данные: их не бэкапят и переиспользуют
# между установками. Сами файлы качает docker/data/pull-models.ps1.
$modelsRoot = $env:MODELS_ROOT

# Не `inspect`: на отсутствующей сети он пишет в stderr, а Windows PowerShell 5.1 считает stderr
# нативной команды ошибкой и под $ErrorActionPreference = "Stop" обрывает скрипт — то есть ровно
# на чистой машине, где этот скрипт и нужен. `ls` с фильтром на пустом месте просто молчит.
if (docker network ls --quiet --filter "name=^$network$") {
    Write-Host "сеть $network уже есть"
} else {
    docker network create $network | Out-Null
    Write-Host "сеть $network создана"
}

foreach ($name in $volumes.Keys) {
    if (docker volume ls --quiet --filter "name=^$name$") {
        Write-Host "том $name уже есть"
        continue
    }

    $path = if ($name -eq "flow-models-data" -and $modelsRoot) {
        $modelsRoot
    } elseif ($dataRoot) {
        Join-Path $dataRoot $volumes[$name]
    } else {
        ""
    }

    if ($path) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        docker volume create --driver local --opt type=none --opt o=bind --opt device=$path $name | Out-Null
        Write-Host "том $name создан на $path"
    } else {
        docker volume create $name | Out-Null
        Write-Host "том $name создан"
    }
}

Write-Host ""
Write-Host "Готово. Дальше:"
Write-Host "  docker compose -f docker-compose.data.yml up -d"
Write-Host "  powershell -ExecutionPolicy Bypass -File docker/data/pull-models.ps1   # веса моделей поиска (профиль ai)"
Write-Host "  docker compose up -d --build"
