#!/usr/bin/env bash
set -euo pipefail

: "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required.}"
: "${RUNNER_TEMP:?RUNNER_TEMP is required.}"
: "${GITHUB_SHA:?GITHUB_SHA is required.}"
: "${GITHUB_RUN_ID:?GITHUB_RUN_ID is required.}"

bash -n deploy/container/install-container.sh
sh -n deploy/container/postgres-init-roles.sh
sh -n deploy/container/start-browser-runtime.sh
bash deploy/container/install-container.sh --help >/dev/null

installer_root="${RUNNER_TEMP}/container-installer-test"
sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
  bash deploy/container/install-container.sh \
  --mode http \
  --tag validation \
  --revision "$GITHUB_SHA" \
  --web-port 18080 \
  --install-dir "$installer_root" \
  --no-start
first_password="$(sudo sed -n 's/^POSTGRES_PASSWORD=//p' "$installer_root/.env")"
first_app_password="$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD=//p' "$installer_root/.env")"
first_maintenance_password="$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD=//p' "$installer_root/.env")"
first_token="$(sudo sed -n 's/^EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=//p' "$installer_root/.env")"

sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
  bash "$installer_root/install-container.sh" \
  --mode http \
  --install-dir "$installer_root" \
  --no-start
test "$first_password" = "$(sudo sed -n 's/^POSTGRES_PASSWORD=//p' "$installer_root/.env")"
test "$first_app_password" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD=//p' "$installer_root/.env")"
test "$first_maintenance_password" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD=//p' "$installer_root/.env")"
test "$first_token" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=//p' "$installer_root/.env")"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_IMAGE_TAG=//p' "$installer_root/.env")" = "validation"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_WEB_PORT=//p' "$installer_root/.env")" = "18080"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_WEB_BIND_ADDRESS=//p' "$installer_root/.env")" = "127.0.0.1"

sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
  bash "$installer_root/install-container.sh" \
  --mode https \
  --domain docs.example.com \
  --email ops@example.com \
  --install-dir "$installer_root" \
  --no-start
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_DEPLOYMENT_MODE=//p' "$installer_root/.env")" = "https"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_WEB_PORT=//p' "$installer_root/.env")" = "80"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_WEB_BIND_ADDRESS=//p' "$installer_root/.env")" = "0.0.0.0"

sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
  bash "$installer_root/install-container.sh" \
  --mode http \
  --web-port 18080 \
  --install-dir "$installer_root" \
  --no-start
test "$first_password" = "$(sudo sed -n 's/^POSTGRES_PASSWORD=//p' "$installer_root/.env")"
test "$first_app_password" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD=//p' "$installer_root/.env")"
test "$first_maintenance_password" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD=//p' "$installer_root/.env")"
test "$first_token" = "$(sudo sed -n 's/^EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=//p' "$installer_root/.env")"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_DEPLOYMENT_MODE=//p' "$installer_root/.env")" = "http"
test "$(sudo sed -n 's/^EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=//p' "$installer_root/.env")" = "false"
test "$(sudo find "$installer_root" -maxdepth 1 -name '.env.previous.*' -print -quit)" = ""

rollback_assets="${RUNNER_TEMP}/container-installer-rollback-assets"
rollback_root="${RUNNER_TEMP}/container-installer-rollback-root"
mkdir -p "$rollback_assets"
mkdir -p "$rollback_root"
cp deploy/container/docker-compose.ghcr.yml "$rollback_assets/"
cp deploy/container/docker-compose.acme.yml "$rollback_assets/"
cp deploy/container/nginx.acme.conf "$rollback_assets/"
cp deploy/container/postgres-init-roles.sh "$rollback_assets/"
cp deploy/container/install-container.sh "$rollback_assets/"
cp deploy/container/deployment-assets.sha256 "$rollback_assets/"
printf 'services: [\n' > "$rollback_assets/docker-compose.ghcr.yml"
printf '# previous environment\n' > "$rollback_root/.env"
printf '# previous compose\n' > "$rollback_root/docker-compose.ghcr.yml"
previous_env_hash="$(sha256sum "$rollback_root/.env" | awk '{print $1}')"
previous_compose_hash="$(sha256sum "$rollback_root/docker-compose.ghcr.yml" | awk '{print $1}')"
if sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://$rollback_assets" \
    bash deploy/container/install-container.sh \
    --mode http \
    --tag validation \
    --revision "$GITHUB_SHA" \
    --install-dir "$rollback_root" \
    --no-start; then
  echo "Installer rollback probe unexpectedly succeeded." >&2
  exit 1
fi
test "$previous_env_hash" = "$(sudo sha256sum "$rollback_root/.env" | awk '{print $1}')"
test "$previous_compose_hash" = "$(sudo sha256sum "$rollback_root/docker-compose.ghcr.yml" | awk '{print $1}')"
test "$(sudo find "$rollback_root" -maxdepth 1 \( -name '.deployment-assets.*' -o -name '.env.previous.*' \) -print -quit)" = ""

symlink_install_target="${RUNNER_TEMP}/container-installer-symlink-target"
symlink_install_path="${RUNNER_TEMP}/container-installer-symlink-root"
mkdir -p "$symlink_install_target"
ln -s "$symlink_install_target" "$symlink_install_path"
if sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
    bash deploy/container/install-container.sh \
    --mode http \
    --tag validation \
    --revision "$GITHUB_SHA" \
    --install-dir "$symlink_install_path" \
    --no-start; then
  echo "Installer unexpectedly accepted a symlink installation root." >&2
  exit 1
fi
test "$(find "$symlink_install_target" -mindepth 1 -print -quit)" = ""

symlink_runtime_root="${RUNNER_TEMP}/container-installer-symlink-runtime-root"
symlink_runtime_target="${RUNNER_TEMP}/container-installer-symlink-runtime-target"
mkdir -p "$symlink_runtime_root" "$symlink_runtime_target"
ln -s "$symlink_runtime_target" "$symlink_runtime_root/runtime"
printf 'EXPORTDOCMANAGER_DEPLOYMENT_MODE=http\nEXPORTDOCMANAGER_RUNTIME_ROOT=%s/runtime\n' \
  "$symlink_runtime_root" > "$symlink_runtime_root/.env"
printf '# previous compose marker\n' > "$symlink_runtime_root/docker-compose.ghcr.yml"
symlink_previous_env_hash="$(sha256sum "$symlink_runtime_root/.env" | awk '{print $1}')"
symlink_previous_compose_hash="$(sha256sum "$symlink_runtime_root/docker-compose.ghcr.yml" | awk '{print $1}')"
if sudo env EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE="file://${GITHUB_WORKSPACE}/deploy/container" \
    bash deploy/container/install-container.sh \
    --mode http \
    --tag validation \
    --revision "$GITHUB_SHA" \
    --install-dir "$symlink_runtime_root" \
    --no-start; then
  echo "Installer unexpectedly accepted a symlink runtime root." >&2
  exit 1
fi
test "$(find "$symlink_runtime_target" -mindepth 1 -print -quit)" = ""
test "$symlink_previous_env_hash" = "$(sudo sha256sum "$symlink_runtime_root/.env" | awk '{print $1}')"
test "$symlink_previous_compose_hash" = "$(sudo sha256sum "$symlink_runtime_root/docker-compose.ghcr.yml" | awk '{print $1}')"
sudo test ! -e "$symlink_runtime_root/docker-compose.acme.yml"
sudo test ! -e "$symlink_runtime_root/nginx.acme.conf"
sudo test ! -e "$symlink_runtime_root/postgres-init-roles.sh"
sudo test ! -e "$symlink_runtime_root/install-container.sh"
test "$(sudo find "$symlink_runtime_root" -maxdepth 1 \( -name '.deployment-assets.*' -o -name '.env.previous.*' \) -print -quit)" = ""

sudo test -f "$installer_root/runtime/api-data/Config/appsettings.json"
test "$(sudo stat -c '%a' "$installer_root")" = "700"
test "$(sudo stat -c '%a' "$installer_root/.env")" = "600"
test "$(sudo stat -c '%a' "$installer_root/runtime")" = "700"
test "$(sudo stat -c '%a' "$installer_root/runtime/postgres")" = "700"
test "$(sudo stat -c '%u:%g' "$installer_root/runtime/postgres")" = "999:999"
test "$(sudo stat -c '%a' "$installer_root/runtime/api-data")" = "750"
test "$(sudo stat -c '%u:%g' "$installer_root/runtime/api-data")" = "10001:10001"
test "$(sudo stat -c '%a' "$installer_root/runtime/api-data/Config")" = "750"
test "$(sudo stat -c '%a' "$installer_root/runtime/api-data/Config/appsettings.json")" = "600"
test "$(sudo stat -c '%u:%g' "$installer_root/runtime/api-data/Config/appsettings.json")" = "10001:10001"
sudo test -d "$installer_root/runtime/api-data/Cache/ReportPdf"
test "$(sudo stat -c '%u:%g' "$installer_root/runtime/api-data/Cache/ReportPdf")" = "10001:10001"
test "$(sudo stat -c '%a' "$installer_root/runtime/api-data/Cache/ReportPdf")" = "750"
sudo test -d "$installer_root/runtime/browser"
test "$(sudo stat -c '%u:%g' "$installer_root/runtime/browser")" = "10001:10001"
test "$(sudo stat -c '%a' "$installer_root/runtime/browser")" = "750"

postgres_probe="exportdoc-installer-postgres-${GITHUB_RUN_ID}"
cleanup_installer_probe() {
  sudo docker rm -f "$postgres_probe" >/dev/null 2>&1 || true
}
trap cleanup_installer_probe EXIT
sudo docker run --detach --name "$postgres_probe" \
  --user 999:999 \
  --env POSTGRES_DB=exportdoc \
  --env POSTGRES_USER=exportdoc \
  --env POSTGRES_PASSWORD=container-installer-db-validation \
  --env 'POSTGRES_INITDB_ARGS=--encoding=UTF8 --locale-provider=builtin --builtin-locale=PG_UNICODE_FAST' \
  --volume "$installer_root/runtime/postgres:/var/lib/postgresql" \
  postgres:18.4-trixie >/dev/null
for attempt in $(seq 1 60); do
  if sudo docker exec "$postgres_probe" pg_isready -U exportdoc -d exportdoc >/dev/null 2>&1; then
    break
  fi
  if [ "$attempt" -eq 60 ]; then
    sudo docker logs "$postgres_probe" >&2 || true
    exit 1
  fi
  sleep 2
done
sudo docker rm -f "$postgres_probe" >/dev/null
sudo docker run --rm \
  --user 10001:10001 \
  --entrypoint sh \
  --volume "$installer_root/runtime/api-data:/runtime-data" \
  postgres:18.4-trixie \
  -ec 'test -r /runtime-data/Config/appsettings.json; probe=/runtime-data/Config/.write-probe-$$; printf ok > "$probe"; mv "$probe" "${probe}.done"; rm "${probe}.done"'
trap - EXIT
sudo chown -R "$(id -u):$(id -g)" "$installer_root"
