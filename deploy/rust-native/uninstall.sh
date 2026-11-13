#!/bin/sh
# Run in the directory containing this deployment's generated compose.yml.
set -eu
cd -- "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
test -f compose.yml || { printf '%s\n' 'Missing compose.yml; use the original deployment directory.' >&2; exit 1; }
docker compose --file compose.yml --profile maintenance down
printf '%s\n' 'Deployment removed. Database volume, data, configuration, backups and images retained.'
