# Создаёт общую сеть и внешние тома данных (Windows). Аналог docker/data/init-env.sh.
#
#   powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1
#   $env:DATA_ROOT="D:\flow"; powershell -ExecutionPolicy Bypass -File docker/data/init-env.ps1
$ErrorActionPreference = "Stop"

$network = "flow-network"
$volumes = @{ "flow-postgres-data" = "postgres"; "flow-minio-data" = "minio" }
$dataRoot = $env:DATA_ROOT

docker network inspect $network *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "сеть $network уже есть"
} else {
    docker network create $network | Out-Null
    Write-Host "сеть $network создана"
}

foreach ($name in $volumes.Keys) {
    docker volume inspect $name *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "том $name уже есть"
        continue
    }

    if ($dataRoot) {
        $path = Join-Path $dataRoot $volumes[$name]
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
Write-Host "  docker compose up -d --build"
