# Поднимает оба стека одной командой (Windows). Аналог docker/up.sh: сеть и тома → данные →
# приложение. Compose не умеет depends_on между проектами, поэтому порядок задаёт скрипт;
# сервис, стартовавший раньше базы, дожидается её сам (Startup:DatabaseWaitTimeoutSeconds).
# Все шаги идемпотентны.
#
#   powershell -ExecutionPolicy Bypass -File docker/up.ps1
#   powershell -ExecutionPolicy Bypass -File docker/up.ps1 -NoBuild
#   $env:DATA_ROOT="D:\flow"; powershell -ExecutionPolicy Bypass -File docker/up.ps1
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Переменные читают оба стека; без файла берутся значения по умолчанию из compose.
if (-not (Test-Path ".env")) {
    Copy-Item ".env.example" ".env"
    Write-Host ".env создан из .env.example — проверьте порты и пароли"
    Write-Host ""
}

powershell -ExecutionPolicy Bypass -File "docker/data/init-env.ps1"
if ($LASTEXITCODE -ne 0) { throw "init-env.ps1 завершился с ошибкой" }

Write-Host ""
Write-Host "== данные =="
docker compose -f docker-compose.data.yml up -d
if ($LASTEXITCODE -ne 0) { throw "стек данных не поднялся" }

Write-Host ""
Write-Host "== приложение =="
if ($NoBuild) { docker compose up -d } else { docker compose up -d --build }
if ($LASTEXITCODE -ne 0) { throw "стек приложения не поднялся" }

# .env читает Compose, а не этот скрипт — порт клиента достаём оттуда только ради сообщения.
$clientPort = (Select-String -Path ".env" -Pattern "^CLIENT_PORT=(.+)$" | Select-Object -Last 1).Matches.Groups[1].Value
if (-not $clientPort) { $clientPort = "5016" }

Write-Host ""
Write-Host "Готово. Клиент: http://localhost:$clientPort"
Write-Host "Состояние:  docker compose ps; docker compose -f docker-compose.data.yml ps"
