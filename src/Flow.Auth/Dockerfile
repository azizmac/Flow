FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Без USER $APP_UID: сервис пишет сертификаты (/certs) и ключи Data Protection (/keys) в volumes,
# которые Docker создаёт от root. При переходе на non-root нужно chown этих каталогов.
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Flow.Auth/Flow.Auth.csproj", "src/Flow.Auth/"]
COPY ["src/Flow.Shared/Flow.Shared.csproj", "src/Flow.Shared/"]
RUN dotnet restore "src/Flow.Auth/Flow.Auth.csproj"
COPY src/ src/
WORKDIR "/src/src/Flow.Auth"
RUN dotnet build "./Flow.Auth.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Flow.Auth.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Flow.Auth.dll"]
