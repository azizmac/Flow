#!/bin/sh
# Пишет wwwroot/appsettings.json из env при старте контейнера: Blazor читает его в браузере,
# поэтому адреса Flow.Api и Flow.Auth должны быть теми, что видит браузер (localhost:…), а не именами сервисов compose.
set -eu

API_BASE_URL="${API_BASE_URL:-http://localhost:8080}"
AUTH_BASE_URL="${AUTH_BASE_URL:-http://localhost:5100}"

cat > /usr/share/nginx/html/appsettings.json <<EOF
{
  "ApiBaseUrl": "${API_BASE_URL}",
  "AuthBaseUrl": "${AUTH_BASE_URL}"
}
EOF

# Сжатая копия из publish иначе перекроет свежий файл (gzip_static).
rm -f /usr/share/nginx/html/appsettings.json.gz /usr/share/nginx/html/appsettings.json.br

echo "Flow.Client: ApiBaseUrl=${API_BASE_URL} AuthBaseUrl=${AUTH_BASE_URL}"
