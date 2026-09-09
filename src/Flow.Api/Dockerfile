FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Flow.Api/Flow.Api.csproj", "src/Flow.Api/"]
COPY ["src/Flow.Application/Flow.Application.csproj", "src/Flow.Application/"]
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
