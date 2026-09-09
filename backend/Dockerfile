# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY . .
RUN --mount=type=cache,id=billing-platform-nuget,target=/root/.nuget/packages \
    dotnet restore BillingPlatform.sln
RUN --mount=type=cache,id=billing-platform-nuget,target=/root/.nuget/packages \
    dotnet publish src/Host/BillingPlatform.Api/BillingPlatform.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP_UID=10001

RUN useradd --uid "${APP_UID}" --create-home --shell /usr/sbin/nologin appuser \
    && apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=appuser:appuser /app/publish .

USER appuser
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "BillingPlatform.Api.dll"]
