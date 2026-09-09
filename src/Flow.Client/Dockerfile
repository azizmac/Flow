# Blazor WebAssembly — статика, поэтому финальный образ не aspnet, а nginx (Rider-шаблон с dotnet Flow.Client.dll здесь не работает).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Flow.Client/Flow.Client.csproj", "src/Flow.Client/"]
COPY ["src/Flow.Shared/Flow.Shared.csproj", "src/Flow.Shared/"]
RUN dotnet restore "src/Flow.Client/Flow.Client.csproj"
COPY src/ src/
WORKDIR "/src/src/Flow.Client"
RUN dotnet publish "./Flow.Client.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# Адреса Flow.Api/Flow.Auth подставляются при старте контейнера из env (docker/client/entrypoint.sh → wwwroot/appsettings.json).
FROM nginx:1.27-alpine AS final
COPY docker/client/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/client/entrypoint.sh /docker-entrypoint.d/40-flow-appsettings.sh
RUN chmod +x /docker-entrypoint.d/40-flow-appsettings.sh
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
