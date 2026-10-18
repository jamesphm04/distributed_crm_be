#!/bin/sh
# Applies the schema migrations, then, when APP_DB_ROLES is set (Azure), creates each app's database role and
# grants it read and write on rows. Connection settings come from the standard PG* environment variables.
set -eu

# Locally PGPASSWORD is set. In Azure, Postgres accepts only Microsoft Entra tokens, so the job asks its managed
# identity for one through the endpoint Container Apps provides (IDENTITY_ENDPOINT, IDENTITY_HEADER).
if [ -z "${PGPASSWORD:-}" ]; then
  : "${IDENTITY_ENDPOINT:?PGPASSWORD is not set and there is no managed identity endpoint}"
  : "${AZURE_CLIENT_ID:?set AZURE_CLIENT_ID to the client ID of the job identity}"
  response="$(curl --silent --show-error --fail --get "$IDENTITY_ENDPOINT" \
    --header "X-IDENTITY-HEADER: $IDENTITY_HEADER" \
    --data-urlencode "resource=https://ossrdbms-aad.database.windows.net" \
    --data-urlencode "api-version=2019-08-01" \
    --data-urlencode "client_id=$AZURE_CLIENT_ID")"
  PGPASSWORD="$(printf '%s' "$response" | sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p')"
  [ -n "$PGPASSWORD" ] || { echo "No access_token in the managed identity response." >&2; exit 1; }
  export PGPASSWORD
fi

psql -v ON_ERROR_STOP=1 -f /migrations/migrate.sql

for role in ${APP_DB_ROLES:-}; do
  # Entra principals are created from the postgres database, then granted access in the application database.
  psql -v ON_ERROR_STOP=1 -v role="$role" -d postgres -f /migrations/create-app-role.sql
  psql -v ON_ERROR_STOP=1 -v role="$role" -f /migrations/grant-app-role.sql
done
