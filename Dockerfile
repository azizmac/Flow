FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Flow.slnx ./
COPY src/Flow.Domain/Flow.Domain.csproj src/Flow.Domain/
COPY src/Flow.Application/Flow.Application.csproj src/Flow.Application/
COPY src/Flow.Infrastructure/Flow.Infrastructure.csproj src/Flow.Infrastructure/
COPY src/Flow.Api/Flow.Api.csproj src/Flow.Api/
COPY src/Flow.Shared/Flow.Shared.csproj src/Flow.Shared/
RUN dotnet restore src/Flow.Api/Flow.Api.csproj

COPY src/ src/
RUN dotnet publish src/Flow.Api/Flow.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "Flow.Api.dll"]
