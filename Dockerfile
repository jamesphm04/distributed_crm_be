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

# --- Migrate: applies the script with psql; connection comes from PG* env vars ---
FROM postgres:17 AS migrate
COPY --from=migrations-script /migrations/migrate.sql /migrations/migrate.sql
ENTRYPOINT ["psql", "-v", "ON_ERROR_STOP=1", "-f", "/migrations/migrate.sql"]

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "DistributedCrm.Api.dll"]