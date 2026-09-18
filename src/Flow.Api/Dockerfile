FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Без USER $APP_UID: Auth-модуль пишет сертификаты (/certs) и ключи Data Protection (/keys) в volumes,
# которые Docker создаёт от root. При переходе на non-root нужно chown этих каталогов.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

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
RUN dotnet restore "src/Flow.Api/Flow.Api.csproj"
COPY src/ src/
WORKDIR "/src/src/Flow.Api"
RUN dotnet build "./Flow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Flow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Flow.Api.dll"]
