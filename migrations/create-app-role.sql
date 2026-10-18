-- Creates a database role for a managed identity, matched to it by the identity's name. Runs as an Entra admin in
-- the postgres database. Skipped when the role exists, so every run of the job is safe.
SELECT pgaadauth_create_principal(:'role', false, false)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role');
