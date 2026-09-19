#!/bin/sh
set -eu
app_password=$(cat /run/secrets/app_password)
maintenance_password=$(cat /run/secrets/maintenance_password)
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=ON_ERROR_STOP=1 --set=app_password="$app_password" --set=maintenance_password="$maintenance_password" <<'SQL'
CREATE ROLE exportdoc_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE exportdoc_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'app_password';
CREATE ROLE exportdoc_maintenance LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'maintenance_password';
GRANT exportdoc_owner TO exportdoc_maintenance;
ALTER DATABASE exportdoc_native OWNER TO exportdoc_owner;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO exportdoc_owner;
GRANT USAGE ON SCHEMA public TO exportdoc_app;
ALTER DEFAULT PRIVILEGES FOR ROLE exportdoc_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO exportdoc_app;
ALTER DEFAULT PRIVILEGES FOR ROLE exportdoc_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO exportdoc_app;
SQL
unset app_password maintenance_password
