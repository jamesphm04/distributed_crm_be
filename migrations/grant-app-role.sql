-- Read and write on rows only: the app can't create, alter or drop anything. The schema belongs to the migration
-- job, which owns every table because it created them. Every statement is safe to repeat.
GRANT CONNECT ON DATABASE :"DBNAME" TO :"role";
GRANT USAGE ON SCHEMA public TO :"role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"role";

-- Tables and sequences that later migrations create get the same grants.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO :"role";

-- The migration history is the job's record of what it applied; an app has no reason to read or change it.
REVOKE ALL ON "__EFMigrationsHistory" FROM :"role";
