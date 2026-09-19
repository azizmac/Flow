FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Без USER $APP_UID: Auth-модуль пишет сертификаты (/certs) и ключи Data Protection (/keys) в volumes,
# которые Docker создаёт от root. При переходе на non-root нужно chown этих каталогов.
WORKDIR /app
EXPOSE 8080

# Markdown-редактор: бандл CodeMirror собирается из исходника, а не берётся готовым из репозитория —
# так образ нельзя собрать с бандлом, отставшим от editor/flow-editor.src.js (см. editor/README.md).
# Стадия переехала сюда из src/Flow.Client/Dockerfile вместе с интерфейсом: отдельного образа клиента
# больше нет, статику отдаёт этот же хост.
FROM node:22-alpine AS editor
WORKDIR /editor
COPY ["src/Flow.Client/editor/package.json", "src/Flow.Client/editor/package-lock.json", "./"]
RUN npm ci --no-audit --no-fund
COPY ["src/Flow.Client/editor/flow-editor.src.js", "./"]
RUN npx esbuild flow-editor.src.js --bundle --minify --format=iife --target=es2020 --outfile=/editor/flow-editor.js

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Flow.Api/Flow.Api.csproj", "src/Flow.Api/"]
COPY ["src/Flow.Application/Flow.Application.csproj", "src/Flow.Application/"]
COPY ["src/Flow.Auth/Flow.Auth.csproj", "src/Flow.Auth/"]
COPY ["src/Flow.Auth.Contracts/Flow.Auth.Contracts.csproj", "src/Flow.Auth.Contracts/"]
COPY ["src/Flow.Domain/Flow.Domain.csproj", "src/Flow.Domain/"]
COPY ["src/Flow.Shared/Flow.Shared.csproj", "src/Flow.Shared/"]
COPY ["src/Flow.Infrastructure/Flow.Infrastructure.csproj", "src/Flow.Infrastructure/"]
COPY ["src/Flow.Client/Flow.Client.csproj", "src/Flow.Client/"]
RUN dotnet restore "src/Flow.Api/Flow.Api.csproj"
COPY src/ src/
# Свежесобранный бандл кладём поверх закоммиченного — в образ попадает он.
COPY --from=editor /editor/flow-editor.js src/Flow.Api/wwwroot/js/flow-editor.js
WORKDIR "/src/src/Flow.Api"
RUN dotnet build "./Flow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Flow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
# Даты интерфейса форматируются по локальному времени процесса (Services/Ru.cs). У контейнера это UTC,
# поэтому пояс задаётся явно: пока интерфейс рендерится на сервере, «локальное время» у всех одно —
# то, что здесь написано. Переопределяется переменной TZ в compose/манифесте.
ENV TZ=Europe/Moscow
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Flow.Api.dll"]
