# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the .csproj files first so `dotnet restore` is cached
# as its own layer, and doesn't re-run just because source code changed.
COPY src/DistributedCrm.Domain/*.csproj src/DistributedCrm.Domain/
COPY src/DistributedCrm.Application/*.csproj src/DistributedCrm.Application/
COPY src/DistributedCrm.Infrastructure/*.csproj src/DistributedCrm.Infrastructure/
COPY src/DistributedCrm.Api/*.csproj src/DistributedCrm.Api/
RUN dotnet restore src/DistributedCrm.Api/DistributedCrm.Api.csproj

# Now copy everything else and publish.
COPY src/ src/
RUN dotnet publish src/DistributedCrm.Api/DistributedCrm.Api.csproj -c Release -o /app

# --- Migrations script: every migration as one idempotent SQL file ---
FROM build AS migrations-script
COPY dotnet-tools.json .
RUN dotnet tool restore
RUN dotnet ef migrations script --idempotent \
    --project src/DistributedCrm.Infrastructure \
    --startup-project src/DistributedCrm.Api \
    --output /migrations/migrate.sql

# --- Migrate: applies the script with psql, then (in Azure) the app roles; connection comes from PG* env vars ---
FROM postgres:17 AS migrate
# curl asks the managed identity for a Postgres token when there is no PGPASSWORD (see migrations/migrate.sh).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=migrations-script /migrations/migrate.sql /migrations/migrate.sql
COPY migrations/migrate.sh migrations/create-app-role.sql migrations/grant-app-role.sql /migrations/
# The image's own unprivileged user; the job only needs to run psql.
USER postgres
ENTRYPOINT ["/migrations/migrate.sh"]

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tini as PID 1. The kernel drops a signal PID 1 sends itself unless it has a handler, so a .NET process that
# crashes (e.g. an unhandled exception at startup) aborts, never dies, and spins at 100% CPU instead of exiting.
# tini runs the app as its child, so a crash exits the container and Container Apps can restart it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

EXPOSE 8080

# Microsoft's .NET images ship a non-root "app" user (APP_UID=1654); never run the app as root.
USER $APP_UID
ENTRYPOINT ["tini", "--", "dotnet", "DistributedCrm.Api.dll"]