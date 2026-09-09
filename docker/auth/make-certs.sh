#!/usr/bin/env sh
# Самоподписанные сертификаты для Flow.Auth в Docker: подпись токенов и шифрование кодов/refresh-токенов.
# В Development сервис использует dev-сертификаты OpenIddict, а в контейнере (Production) PFX обязательны.
# Использование: sh docker/auth/make-certs.sh [password] — по умолчанию пароль flow-certs (как AUTH_CERT_PASSWORD в compose).
set -eu

DIR="$(cd "$(dirname "$0")" && pwd)/certs"
PASSWORD="${1:-flow-certs}"
mkdir -p "$DIR"

make_cert() {
  name="$1"
  cn="$2"
  openssl req -x509 -newkey rsa:2048 -sha256 -days 730 -nodes \
    -subj "/CN=$cn" \
    -keyout "$DIR/$name.key" -out "$DIR/$name.crt" >/dev/null 2>&1
  openssl pkcs12 -export -inkey "$DIR/$name.key" -in "$DIR/$name.crt" \
    -out "$DIR/$name.pfx" -passout "pass:$PASSWORD"
  rm -f "$DIR/$name.key" "$DIR/$name.crt"
  echo "created $DIR/$name.pfx"
}

make_cert signing "Flow.Auth signing"
make_cert encryption "Flow.Auth encryption"
