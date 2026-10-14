#!/bin/sh
set -eu
secrets_root=${EXPORTDOCMANAGER_SECRETS_ROOT:-/run/secrets}
app_password=$(cat "$secrets_root/app_password")
maintenance_password=$(cat "$secrets_root/maintenance_password")
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=ON_ERROR_STOP=1 \
    --set=app_password="$app_password" --set=maintenance_password="$maintenance_password" \
    --single-transaction --file=/docker-entrypoint-initdb.d/sql/postgres-roles.sql
unset app_password maintenance_password
