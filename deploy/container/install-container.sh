#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

MODE=""
DEFAULT_INSTALL_DIR="/opt/export-doc-manager"
if [[ -f ${BASH_SOURCE[0]:-} ]]; then
  SCRIPT_DIRECTORY=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
  [[ -f "$SCRIPT_DIRECTORY/.env" ]] && DEFAULT_INSTALL_DIR=$SCRIPT_DIRECTORY
fi
INSTALL_DIR=$DEFAULT_INSTALL_DIR
IMAGE_NAMESPACE=""
IMAGE_TAG=""
IMAGE_REVISION=""
WEB_PORT=""
WEB_BIND_ADDRESS=""
PUBLIC_DOMAIN=""
ACME_EMAIL=""
REPOSITORY_REF="main"
CONTAINER_SUBNET=""
ALLOW_NETWORK_OVERLAP=0
ALLOW_INSECURE_DISASTER_RECOVERY=0
NO_START=0

usage() {
  cat <<'EOF'
ExportDocManager container installer for a Linux VPS

Usage:
  install-container.sh --mode http [options]
  install-container.sh --mode https --domain docs.example.com --email ops@example.com [options]

Options:
  --mode http|https          Internal HTTP or public Nginx + automatic HTTPS
  --tag TAG                  Required exact GHCR image tag (latest is rejected)
  --revision SHA             Required 40-character source revision from the release manifest
  --image-namespace VALUE    Image namespace (default: ghcr.io/sck03)
  --install-dir PATH         Deployment and runtime root (default: /opt/export-doc-manager)
  --web-port PORT            Internal HTTP host port (default: 8080)
  --web-bind-address ADDRESS HTTP bind address (default: 127.0.0.1)
  --allow-insecure-disaster-recovery
                              Explicitly allow sensitive recovery uploads over HTTP
  --domain DOMAIN            Public DNS name for HTTPS mode
  --email EMAIL              ACME expiry notice email for HTTPS mode
  --repo-ref REF             Git ref used to download deployment assets (default: main)
  --subnet CIDR              Explicit private /24 to /28 Docker subnet
  --allow-network-overlap    Accept an explicitly configured overlapping subnet
  --no-start                 Generate and validate files without pulling or starting containers
  -h, --help                 Show this help

For private GHCR packages, export GHCR_USER and GHCR_TOKEN before running.
Optional first-install secrets can be supplied through
EXPORTDOCMANAGER_INSTALL_POSTGRES_PASSWORD, EXPORTDOCMANAGER_INSTALL_POSTGRES_APP_PASSWORD,
EXPORTDOCMANAGER_INSTALL_POSTGRES_MAINTENANCE_PASSWORD and EXPORTDOCMANAGER_INSTALL_BOOTSTRAP_TOKEN.
EOF
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

note() {
  printf '%s\n' "$*"
}

assert_safe_directory_path() {
  local candidate=$1
  local label=$2
  local parent
  [[ "$candidate" == /* ]] || fail "$label must be an absolute path."

  while :; do
    [[ ! -L "$candidate" ]] || fail "$label must not traverse a symbolic link: $candidate"
    [[ ! -e "$candidate" || -d "$candidate" ]] || fail "$label must not traverse a regular file: $candidate"
    [[ "$candidate" != "/" ]] || break
    parent=${candidate%/*}
    [[ -n "$parent" ]] || parent=/
    [[ "$parent" != "$candidate" ]] || break
    candidate=$parent
  done
}

while (($# > 0)); do
  case "$1" in
    --mode) MODE=${2:?Missing value for --mode}; shift 2 ;;
    --tag) IMAGE_TAG=${2:?Missing value for --tag}; shift 2 ;;
    --revision) IMAGE_REVISION=${2:?Missing value for --revision}; shift 2 ;;
    --image-namespace) IMAGE_NAMESPACE=${2:?Missing value for --image-namespace}; shift 2 ;;
    --install-dir) INSTALL_DIR=${2:?Missing value for --install-dir}; shift 2 ;;
    --web-port) WEB_PORT=${2:?Missing value for --web-port}; shift 2 ;;
    --web-bind-address) WEB_BIND_ADDRESS=${2:?Missing value for --web-bind-address}; shift 2 ;;
    --allow-insecure-disaster-recovery) ALLOW_INSECURE_DISASTER_RECOVERY=1; shift ;;
    --domain) PUBLIC_DOMAIN=${2:?Missing value for --domain}; shift 2 ;;
    --email) ACME_EMAIL=${2:?Missing value for --email}; shift 2 ;;
    --repo-ref) REPOSITORY_REF=${2:?Missing value for --repo-ref}; shift 2 ;;
    --subnet) CONTAINER_SUBNET=${2:?Missing value for --subnet}; shift 2 ;;
    --allow-network-overlap) ALLOW_NETWORK_OVERLAP=1; shift ;;
    --no-start) NO_START=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Unknown argument: $1" ;;
  esac
done

[[ $(id -u) -eq 0 ]] || fail "Run this installer as root, for example with sudo."
[[ "$INSTALL_DIR" == /* && "$INSTALL_DIR" != "/" && ! "$INSTALL_DIR" =~ [[:space:]] ]] ||
  fail "--install-dir must be an absolute non-root path without whitespace."
[[ "$REPOSITORY_REF" =~ ^[A-Za-z0-9._/-]+$ && "$REPOSITORY_REF" != *..* ]] || fail "Invalid Git ref."
while [[ "$INSTALL_DIR" != "/" && "$INSTALL_DIR" == */ ]]; do
  INSTALL_DIR=${INSTALL_DIR%/}
done
assert_safe_directory_path "$INSTALL_DIR" "Installation directory"

case "$(uname -m)" in
  x86_64|amd64|aarch64|arm64) ;;
  *) fail "Only Linux x64 and ARM64 hosts are supported by the published images." ;;
esac

for command_name in curl awk sed chmod chown cp mkdir mktemp mv openssl rm; do
  command -v "$command_name" >/dev/null 2>&1 || fail "Required command is missing: $command_name"
done

command -v docker >/dev/null 2>&1 ||
  fail "Docker is not installed. Install Docker Engine and Compose v2 from your Linux distribution or Docker's signed package repository first."
if ! docker info >/dev/null 2>&1; then
  if command -v systemctl >/dev/null 2>&1; then
    systemctl enable --now docker
  fi
fi
docker info >/dev/null 2>&1 || fail "Docker Engine is not running."
docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is required (docker compose)."

mkdir -p -- "$INSTALL_DIR"
assert_safe_directory_path "$INSTALL_DIR" "Installation directory"
INSTALL_DIR=$(cd -- "$INSTALL_DIR" && pwd -P)
[[ "$INSTALL_DIR" != "/" ]] || fail "The resolved installation directory must not be the filesystem root."
chown root:root "$INSTALL_DIR"
chmod 700 "$INSTALL_DIR"
if command -v flock >/dev/null 2>&1; then
  INSTALL_LOCK="$INSTALL_DIR/.install.lock"
  [[ ! -L "$INSTALL_LOCK" ]] || fail "Installer lock must not be a symbolic link: $INSTALL_LOCK"
  [[ ! -e "$INSTALL_LOCK" || -f "$INSTALL_LOCK" ]] || fail "Installer lock must be a regular file: $INSTALL_LOCK"
  exec 9>"$INSTALL_LOCK"
  flock -n 9 || fail "Another installer process is using $INSTALL_DIR."
fi

ASSET_BASE=${EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE:-"https://raw.githubusercontent.com/sck03/rustdoc/${REPOSITORY_REF}/deploy/container"}
CHECKSUM_MANIFEST=deployment-assets.sha256
DEPLOYMENT_ASSETS=(docker-compose.ghcr.yml docker-compose.acme.yml nginx.acme.conf postgres-init-roles.sh install-container.sh)
MANAGED_DEPLOYMENT_ASSETS=("$CHECKSUM_MANIFEST" "${DEPLOYMENT_ASSETS[@]}")
ENVIRONMENT_FILE="$INSTALL_DIR/.env"
for managed_file in "${MANAGED_DEPLOYMENT_ASSETS[@]}" .env; do
  managed_path="$INSTALL_DIR/$managed_file"
  [[ ! -L "$managed_path" ]] || fail "Managed deployment files must not be symbolic links: $managed_path"
  [[ ! -e "$managed_path" || -f "$managed_path" ]] || fail "Managed deployment paths must be regular files: $managed_path"
done
ASSET_STAGE=$(mktemp -d "$INSTALL_DIR/.deployment-assets.stage.XXXXXX")
ASSET_BACKUP=$(mktemp -d "$INSTALL_DIR/.deployment-assets.previous.XXXXXX")
ENVIRONMENT_BACKUP=""
ENVIRONMENT_FILE_WAS_PRESENT=0
ACTIVATION_FILE=""
ENVIRONMENT_TEMP_FILE=""
CERTIFICATE_BACKUP=""
CERTIFICATE_STATE_CAPTURED=0
ROLLBACK_REQUIRED=0
DEPLOYMENT_SUCCEEDED=0

restore_deployment_assets() {
  local asset
  for asset in "${MANAGED_DEPLOYMENT_ASSETS[@]}"; do
    if [[ -f "$ASSET_BACKUP/$asset" ]]; then
      cp -p -- "$ASSET_BACKUP/$asset" "$INSTALL_DIR/$asset"
    elif [[ -f "$ASSET_BACKUP/.missing-$asset" ]]; then
      rm -f -- "$INSTALL_DIR/$asset"
    fi
  done
}

restore_certificate_state() {
  ((CERTIFICATE_STATE_CAPTURED == 1)) || return 0
  [[ -n ${CERTIFICATE_ROOT:-} && -n "$CERTIFICATE_BACKUP" && -d "$CERTIFICATE_BACKUP" ]] || return 1

  local failed_certificate_parent
  local failed_certificate_root
  local active_state_moved=0
  failed_certificate_parent=$(mktemp -d "$RUNTIME_ROOT/.letsencrypt.failed.XXXXXX") || return 1
  failed_certificate_root="$failed_certificate_parent/current"
  if [[ -e "$CERTIFICATE_ROOT" ]]; then
    if ! mv -- "$CERTIFICATE_ROOT" "$failed_certificate_root"; then
      rmdir -- "$failed_certificate_parent" || true
      return 1
    fi
    active_state_moved=1
  fi
  if mv -- "$CERTIFICATE_BACKUP" "$CERTIFICATE_ROOT"; then
    CERTIFICATE_BACKUP=""
    CERTIFICATE_STATE_CAPTURED=0
    rm -rf -- "$failed_certificate_parent"
    return 0
  fi

  if ((active_state_moved == 1)) &&
      mv -- "$failed_certificate_root" "$CERTIFICATE_ROOT"; then
    rmdir -- "$failed_certificate_parent" || true
  elif ((active_state_moved == 0)); then
    rmdir -- "$failed_certificate_parent" || true
  else
    note "WARNING: Failed certificate state was preserved at $failed_certificate_parent." >&2
  fi
  return 1
}

restore_previous_deployment() {
  note "Installation failed; restoring the previous deployment files and environment..." >&2
  if ! restore_certificate_state; then
    note "WARNING: The previous certificate state could not be restored automatically; inspect $RUNTIME_ROOT before exposing HTTPS." >&2
  fi
  if [[ -n "$ENVIRONMENT_BACKUP" && -f "$ENVIRONMENT_BACKUP" ]]; then
    if ((ENVIRONMENT_FILE_WAS_PRESENT == 1)); then
      cp -- "$ENVIRONMENT_BACKUP" "$INSTALL_DIR/.env"
      chmod 600 "$INSTALL_DIR/.env"
    else
      rm -f -- "$INSTALL_DIR/.env"
    fi
  fi
  restore_deployment_assets

  local previous_mode=${EXISTING_MODE:-}
  [[ "$previous_mode" == "http" || "$previous_mode" == "https" ]] || return 0
  local previous_compose=(docker compose -f "$INSTALL_DIR/docker-compose.ghcr.yml")
  if [[ "$previous_mode" == "https" ]]; then
    previous_compose+=(-f "$INSTALL_DIR/docker-compose.acme.yml")
  fi
  previous_compose+=(--env-file "$INSTALL_DIR/.env")
  if "${previous_compose[@]}" config --quiet >/dev/null 2>&1 &&
      "${previous_compose[@]}" up -d --remove-orphans --force-recreate >/dev/null 2>&1; then
    note "The previous $previous_mode deployment was restored."
  else
    note "WARNING: The previous deployment files were restored, but its containers could not be restarted automatically; inspect Docker Compose state in $INSTALL_DIR." >&2
  fi
}

cleanup() {
  local exit_code=$?
  trap - EXIT
  if ((exit_code != 0 && ROLLBACK_REQUIRED == 1 && DEPLOYMENT_SUCCEEDED == 0)); then
    restore_previous_deployment || true
  fi
  [[ -z "$ENVIRONMENT_BACKUP" ]] || rm -f -- "$ENVIRONMENT_BACKUP"
  [[ -z "$ACTIVATION_FILE" ]] || rm -f -- "$ACTIVATION_FILE"
  [[ -z "$ENVIRONMENT_TEMP_FILE" ]] || rm -f -- "$ENVIRONMENT_TEMP_FILE"
  if [[ -n "$CERTIFICATE_BACKUP" ]]; then
    if ((exit_code != 0 && CERTIFICATE_STATE_CAPTURED == 1)); then
      note "WARNING: A certificate rollback backup was preserved for manual recovery: $CERTIFICATE_BACKUP" >&2
    else
      rm -rf -- "$CERTIFICATE_BACKUP"
    fi
  fi
  rm -rf -- "$ASSET_STAGE" "$ASSET_BACKUP"
  exit "$exit_code"
}
trap cleanup EXIT

download_asset() {
  local name=$1
  local destination="$ASSET_STAGE/$name"
  curl --fail --silent --show-error --location --retry 3 "$ASSET_BASE/$name" --output "$destination"
  [[ -s "$destination" ]] || fail "Downloaded deployment asset is empty: $name"
  chmod 600 "$destination"
}

verify_deployment_manifest() {
  local manifest="$ASSET_STAGE/$CHECKSUM_MANIFEST"
  local entry_count=0
  local hash
  local name
  local extra
  local expected
  local actual

  while read -r hash name extra; do
    [[ -n "$hash" ]] || continue
    [[ "$hash" != \#* ]] || continue
    [[ "$hash" =~ ^[0-9A-Fa-f]{64}$ && -n "$name" && -z "$extra" ]] ||
      fail "Deployment checksum manifest has an invalid entry."
    case " ${DEPLOYMENT_ASSETS[*]} " in
      *" $name "*) ;;
      *) fail "Deployment checksum manifest contains an unmanaged asset: $name" ;;
    esac
    ((entry_count += 1))
  done < "$manifest"
  ((entry_count == ${#DEPLOYMENT_ASSETS[@]})) ||
    fail "Deployment checksum manifest must contain exactly ${#DEPLOYMENT_ASSETS[@]} managed assets."

  for name in "${DEPLOYMENT_ASSETS[@]}"; do
    expected=$(awk -v name="$name" '$2 == name { print tolower($1) }' "$manifest")
    [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || fail "Deployment checksum is missing or duplicated: $name"
    actual=$(openssl dgst -sha256 "$ASSET_STAGE/$name" | awk '{ print tolower($NF) }')
    [[ "$actual" == "$expected" ]] || fail "Deployment asset checksum mismatch: $name"
  done
}

download_asset "$CHECKSUM_MANIFEST"
for asset in "${DEPLOYMENT_ASSETS[@]}"; do
  download_asset "$asset"
done
verify_deployment_manifest
bash -n "$ASSET_STAGE/install-container.sh"
sh -n "$ASSET_STAGE/postgres-init-roles.sh"
STAGED_VALIDATION_ENV="$ASSET_STAGE/.compose-validation.env"
cat > "$STAGED_VALIDATION_ENV" <<EOF
POSTGRES_DB=exportdoc
POSTGRES_USER=exportdoc
POSTGRES_PASSWORD=staged-compose-validation-password
EXPORTDOCMANAGER_POSTGRES_APP_USER=exportdoc_app
EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD=staged-compose-validation-app-password
EXPORTDOCMANAGER_POSTGRES_OWNER_ROLE=exportdoc_owner
EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_USER=exportdoc_maintenance
EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD=staged-compose-validation-maintenance-password
EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=staged-compose-validation-bootstrap-token
EXPORTDOCMANAGER_IMAGE_NAMESPACE=ghcr.io/sck03
EXPORTDOCMANAGER_IMAGE_TAG=validation
EXPORTDOCMANAGER_IMAGE_REVISION=0000000000000000000000000000000000000000
EXPORTDOCMANAGER_RUNTIME_ROOT=$INSTALL_DIR/.compose-validation-runtime
EXPORTDOCMANAGER_WEB_PORT=18080
EXPORTDOCMANAGER_WEB_BIND_ADDRESS=127.0.0.1
EXPORTDOCMANAGER_HTTPS_PORT=18443
EXPORTDOCMANAGER_CONTAINER_SUBNET=172.31.255.0/28
EXPORTDOCMANAGER_REVERSE_PROXY_IP=172.31.255.10
EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=false
EOF
chmod 600 "$STAGED_VALIDATION_ENV"
docker compose \
  -f "$ASSET_STAGE/docker-compose.ghcr.yml" \
  --env-file "$STAGED_VALIDATION_ENV" \
  config --quiet
docker compose \
  -f "$ASSET_STAGE/docker-compose.ghcr.yml" \
  -f "$ASSET_STAGE/docker-compose.acme.yml" \
  --env-file "$STAGED_VALIDATION_ENV" \
  config --quiet

for asset in "${MANAGED_DEPLOYMENT_ASSETS[@]}"; do
  if [[ -f "$INSTALL_DIR/$asset" ]]; then
    cp -p -- "$INSTALL_DIR/$asset" "$ASSET_BACKUP/$asset"
  else
    : > "$ASSET_BACKUP/.missing-$asset"
  fi
done

if [[ -f "$ENVIRONMENT_FILE" ]]; then
  ENVIRONMENT_FILE_WAS_PRESENT=1
fi
ENVIRONMENT_BACKUP=$(mktemp "$INSTALL_DIR/.env.previous.XXXXXX")
if ((ENVIRONMENT_FILE_WAS_PRESENT == 1)); then
  cp -- "$ENVIRONMENT_FILE" "$ENVIRONMENT_BACKUP"
else
  : > "$ENVIRONMENT_BACKUP"
fi
chmod 600 "$ENVIRONMENT_BACKUP"
ROLLBACK_REQUIRED=1

for asset in "${MANAGED_DEPLOYMENT_ASSETS[@]}"; do
  ACTIVATION_FILE=$(mktemp "$INSTALL_DIR/.$asset.activate.XXXXXX")
  cp -- "$ASSET_STAGE/$asset" "$ACTIVATION_FILE"
  chmod 600 "$ACTIVATION_FILE"
  mv -f -- "$ACTIVATION_FILE" "$INSTALL_DIR/$asset"
  ACTIVATION_FILE=""
done
chmod 700 "$INSTALL_DIR/install-container.sh"
chmod 644 "$INSTALL_DIR/postgres-init-roles.sh"

if ((ENVIRONMENT_FILE_WAS_PRESENT == 0)); then
  : > "$ENVIRONMENT_FILE"
  chmod 600 "$ENVIRONMENT_FILE"
fi

env_value() {
  local key=$1
  awk -F= -v key="$key" '$1 == key { value=substr($0, index($0, "=") + 1) } END { print value }' "$ENVIRONMENT_FILE"
}

set_env_value() {
  local key=$1
  local value=$2
  ENVIRONMENT_TEMP_FILE=$(mktemp "$INSTALL_DIR/.env.tmp.XXXXXX")
  ENV_VALUE="$value" awk -v key="$key" '
    BEGIN { replaced=0; value=ENVIRON["ENV_VALUE"] }
    index($0, key "=") == 1 {
      if (!replaced) print key "=" value
      replaced=1
      next
    }
    { print }
    END { if (!replaced) print key "=" value }
  ' "$ENVIRONMENT_FILE" > "$ENVIRONMENT_TEMP_FILE"
  chmod 600 "$ENVIRONMENT_TEMP_FILE"
  mv -f -- "$ENVIRONMENT_TEMP_FILE" "$ENVIRONMENT_FILE"
  ENVIRONMENT_TEMP_FILE=""
}

ipv4_to_int() {
  local address=$1 a b c d octet
  IFS=. read -r a b c d <<< "$address"
  [[ -n ${d:-} && $a =~ ^[0-9]{1,3}$ && $b =~ ^[0-9]{1,3}$ && $c =~ ^[0-9]{1,3}$ && $d =~ ^[0-9]{1,3}$ ]] || return 1
  for octet in "$a" "$b" "$c" "$d"; do
    [[ "$octet" == "0" || "$octet" != 0* ]] || return 1
  done
  ((10#$a <= 255 && 10#$b <= 255 && 10#$c <= 255 && 10#$d <= 255)) || return 1
  printf '%u\n' "$(((10#$a << 24) + (10#$b << 16) + (10#$c << 8) + 10#$d))"
}

EXISTING_MODE=$(env_value EXPORTDOCMANAGER_DEPLOYMENT_MODE)
MODE=${MODE:-$EXISTING_MODE}
MODE=${MODE:-http}
[[ "$MODE" == "http" || "$MODE" == "https" ]] || fail "--mode must be http or https."

IMAGE_NAMESPACE=${IMAGE_NAMESPACE:-$(env_value EXPORTDOCMANAGER_IMAGE_NAMESPACE)}
IMAGE_NAMESPACE=${IMAGE_NAMESPACE:-ghcr.io/sck03}
IMAGE_TAG=${IMAGE_TAG:-$(env_value EXPORTDOCMANAGER_IMAGE_TAG)}
IMAGE_REVISION=${IMAGE_REVISION:-$(env_value EXPORTDOCMANAGER_IMAGE_REVISION)}
[[ -n "$IMAGE_TAG" ]] || fail "First installation requires --tag with an exact published image version."
[[ -n "$IMAGE_REVISION" ]] || fail "First installation requires --revision from the immutable container release manifest."
if [[ -z "$WEB_PORT" && "$MODE" == "http" && "$EXISTING_MODE" == "http" ]]; then
  EXISTING_WEB_PORT=$(env_value EXPORTDOCMANAGER_WEB_PORT)
  [[ "$EXISTING_WEB_PORT" =~ ^[0-9]+$ ]] && WEB_PORT=$EXISTING_WEB_PORT
fi
WEB_PORT=${WEB_PORT:-8080}
[[ "$WEB_PORT" =~ ^[0-9]+$ ]] && ((WEB_PORT >= 1 && WEB_PORT <= 65535)) ||
  fail "--web-port must be between 1 and 65535."
if [[ -z "$WEB_BIND_ADDRESS" && "$MODE" == "$EXISTING_MODE" ]]; then
  WEB_BIND_ADDRESS=$(env_value EXPORTDOCMANAGER_WEB_BIND_ADDRESS)
fi
if [[ "$MODE" == "https" ]]; then
  WEB_BIND_ADDRESS=${WEB_BIND_ADDRESS:-0.0.0.0}
else
  WEB_BIND_ADDRESS=${WEB_BIND_ADDRESS:-127.0.0.1}
fi
ipv4_to_int "$WEB_BIND_ADDRESS" >/dev/null ||
  fail "--web-bind-address must be an IPv4 address such as 127.0.0.1 or 0.0.0.0."
if [[ "$MODE" == "http" && "$WEB_BIND_ADDRESS" != 127.* && $ALLOW_INSECURE_DISASTER_RECOVERY -eq 1 ]]; then
  note "WARNING: HTTP recovery uploads are explicitly enabled on a non-loopback interface; use HTTPS for untrusted networks." >&2
fi
[[ "$IMAGE_NAMESPACE" =~ ^ghcr\.io/[a-z0-9][a-z0-9._-]*$ ]] ||
  fail "--image-namespace must look like ghcr.io/account."
[[ "$IMAGE_TAG" =~ ^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$ ]] || fail "Invalid container image tag."
[[ "$IMAGE_TAG" != "latest" ]] || fail "The mutable latest tag is not accepted; use an exact published image version."
[[ "$IMAGE_REVISION" =~ ^[0-9a-fA-F]{40}$ ]] || fail "--revision must be a complete 40-character Git commit SHA."
IMAGE_REVISION=${IMAGE_REVISION,,}

RUNTIME_ROOT=$(env_value EXPORTDOCMANAGER_RUNTIME_ROOT)
RUNTIME_ROOT=${RUNTIME_ROOT:-$INSTALL_DIR/runtime}
[[ "$RUNTIME_ROOT" == /* && "$RUNTIME_ROOT" != "/" && ! "$RUNTIME_ROOT" =~ [[:space:]] ]] ||
  fail "Existing EXPORTDOCMANAGER_RUNTIME_ROOT must be an absolute non-root path without whitespace."
while [[ "$RUNTIME_ROOT" != "/" && "$RUNTIME_ROOT" == */ ]]; do
  RUNTIME_ROOT=${RUNTIME_ROOT%/}
done
assert_safe_directory_path "$RUNTIME_ROOT" "Runtime data root"

PUBLIC_DOMAIN=${PUBLIC_DOMAIN:-$(env_value EXPORTDOCMANAGER_PUBLIC_DOMAIN)}
ACME_EMAIL=${ACME_EMAIL:-$(env_value EXPORTDOCMANAGER_ACME_EMAIL)}
valid_domain() {
  local domain=$1 label
  [[ ${#domain} -le 253 && "$domain" == *.* && "$domain" != *..* ]] || return 1
  IFS=. read -r -a labels <<< "$domain"
  for label in "${labels[@]}"; do
    [[ ${#label} -ge 1 && ${#label} -le 63 && "$label" =~ ^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?$ ]] || return 1
  done
}
if [[ "$MODE" == "https" ]]; then
  PUBLIC_DOMAIN=${PUBLIC_DOMAIN,,}
  valid_domain "$PUBLIC_DOMAIN" ||
    fail "HTTPS mode requires --domain with a DNS name, without http:// or a path."
  [[ "$ACME_EMAIL" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]] ||
    fail "HTTPS mode requires a valid --email address for certificate notices."
fi

random_hex() {
  local bytes=$1
  openssl rand -hex "$bytes"
}

POSTGRES_PASSWORD=$(env_value POSTGRES_PASSWORD)
POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-${EXPORTDOCMANAGER_INSTALL_POSTGRES_PASSWORD:-}}
POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-$(random_hex 24)}
POSTGRES_APP_PASSWORD=$(env_value EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD)
POSTGRES_APP_PASSWORD=${POSTGRES_APP_PASSWORD:-${EXPORTDOCMANAGER_INSTALL_POSTGRES_APP_PASSWORD:-}}
POSTGRES_APP_PASSWORD=${POSTGRES_APP_PASSWORD:-$(random_hex 24)}
POSTGRES_MAINTENANCE_PASSWORD=$(env_value EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD)
POSTGRES_MAINTENANCE_PASSWORD=${POSTGRES_MAINTENANCE_PASSWORD:-${EXPORTDOCMANAGER_INSTALL_POSTGRES_MAINTENANCE_PASSWORD:-}}
POSTGRES_MAINTENANCE_PASSWORD=${POSTGRES_MAINTENANCE_PASSWORD:-$(random_hex 24)}
BOOTSTRAP_TOKEN=$(env_value EXPORTDOCMANAGER_BOOTSTRAP_TOKEN)
BOOTSTRAP_TOKEN=${BOOTSTRAP_TOKEN:-${EXPORTDOCMANAGER_INSTALL_BOOTSTRAP_TOKEN:-}}
BOOTSTRAP_TOKEN=${BOOTSTRAP_TOKEN:-$(random_hex 32)}
(( ${#POSTGRES_PASSWORD} >= 12 )) && [[ "$POSTGRES_PASSWORD" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "PostgreSQL administrator password is invalid or shorter than 12 characters."
(( ${#POSTGRES_APP_PASSWORD} >= 12 )) && [[ "$POSTGRES_APP_PASSWORD" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "PostgreSQL application password is invalid or shorter than 12 characters."
(( ${#POSTGRES_MAINTENANCE_PASSWORD} >= 12 )) && [[ "$POSTGRES_MAINTENANCE_PASSWORD" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "PostgreSQL maintenance password is invalid or shorter than 12 characters."
[[ "$POSTGRES_PASSWORD" != "$POSTGRES_APP_PASSWORD" &&
   "$POSTGRES_PASSWORD" != "$POSTGRES_MAINTENANCE_PASSWORD" &&
   "$POSTGRES_APP_PASSWORD" != "$POSTGRES_MAINTENANCE_PASSWORD" ]] ||
  fail "PostgreSQL administrator, application, and maintenance passwords must be distinct."
(( ${#BOOTSTRAP_TOKEN} >= 24 && ${#BOOTSTRAP_TOKEN} <= 512 )) && [[ "$BOOTSTRAP_TOKEN" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "Bootstrap token must contain 24-512 safe characters."

int_to_ipv4() {
  local value=$1
  printf '%d.%d.%d.%d\n' "$(((value >> 24) & 255))" "$(((value >> 16) & 255))" "$(((value >> 8) & 255))" "$((value & 255))"
}

cidr_bounds() {
  local cidr=$1 address prefix address_number size start
  address=${cidr%/*}
  prefix=${cidr#*/}
  [[ "$prefix" =~ ^[0-9]+$ ]] && ((prefix >= 1 && prefix <= 32)) || return 1
  address_number=$(ipv4_to_int "$address") || return 1
  size=$((1 << (32 - prefix)))
  start=$((address_number / size * size))
  printf '%u %u %u %u\n' "$start" "$((start + size - 1))" "$prefix" "$address_number"
}

declare -a OBSERVED_RANGES=()
add_observed_range() {
  local cidr=$1 bounds
  [[ "$cidr" == */* && "$cidr" != "0.0.0.0/0" ]] || return 0
  bounds=$(cidr_bounds "$cidr") || return 0
  OBSERVED_RANGES+=("$bounds")
}

if command -v ip >/dev/null 2>&1; then
  while read -r cidr; do add_observed_range "$cidr"; done < <(ip -o -4 address show | awk '{print $4}')
  while read -r cidr; do add_observed_range "$cidr"; done < <(ip -o -4 route show | awk '$1 ~ /^[0-9]+\./ && $1 ~ /\// {print $1}')
fi
mapfile -t DOCKER_NETWORK_IDS < <(docker network ls --quiet)
if ((${#DOCKER_NETWORK_IDS[@]} > 0)); then
  while read -r cidr; do add_observed_range "$cidr"; done < <(
    docker network inspect "${DOCKER_NETWORK_IDS[@]}" --format '{{range .IPAM.Config}}{{println .Subnet}}{{end}}' 2>/dev/null || true
  )
fi

range_overlaps_observed() {
  local bounds=$1 start end observed observed_start observed_end
  read -r start end _ <<< "$bounds"
  for observed in "${OBSERVED_RANGES[@]}"; do
    read -r observed_start observed_end _ <<< "$observed"
    if ((start <= observed_end && observed_start <= end)); then
      return 0
    fi
  done
  return 1
}

select_available_subnet() {
  local second block candidate bounds third
  for second in {20..31}; do
    for block in {0..15}; do
      candidate="172.${second}.238.$((block * 16))/28"
      bounds=$(cidr_bounds "$candidate")
      if ! range_overlaps_observed "$bounds"; then printf '%s\n' "$candidate"; return; fi
    done
  done
  for third in {0..255}; do
    candidate="10.238.${third}.0/28"
    bounds=$(cidr_bounds "$candidate")
    if ! range_overlaps_observed "$bounds"; then printf '%s\n' "$candidate"; return; fi
  done
  fail "No non-overlapping private Docker /28 subnet was found; use --subnet after checking local and VPN routes."
}

EXISTING_SUBNET=$(env_value EXPORTDOCMANAGER_CONTAINER_SUBNET)
CONTAINER_SUBNET=${CONTAINER_SUBNET:-$EXISTING_SUBNET}
EXPLICIT_SUBNET=1
if [[ -z "$CONTAINER_SUBNET" ]]; then
  EXPLICIT_SUBNET=0
  CONTAINER_SUBNET=$(select_available_subnet)
fi

read -r SUBNET_START SUBNET_END SUBNET_PREFIX SUBNET_INPUT <<< "$(cidr_bounds "$CONTAINER_SUBNET" || true)"
[[ -n ${SUBNET_START:-} && $SUBNET_PREFIX -ge 24 && $SUBNET_PREFIX -le 28 && $SUBNET_START -eq $SUBNET_INPUT ]] ||
  fail "Container subnet must be an aligned private IPv4 /24 to /28."
((SUBNET_START >= 167772160 && SUBNET_END <= 184549375 ||
  SUBNET_START >= 2886729728 && SUBNET_END <= 2887778303 ||
  SUBNET_START >= 3232235520 && SUBNET_END <= 3232301055)) || fail "Container subnet must be inside an RFC 1918 private range."
if ((EXPLICIT_SUBNET == 1 && ALLOW_NETWORK_OVERLAP == 0)) && [[ "$CONTAINER_SUBNET" != "$EXISTING_SUBNET" ]] &&
  range_overlaps_observed "$SUBNET_START $SUBNET_END $SUBNET_PREFIX $SUBNET_INPUT"; then
  fail "Explicit subnet overlaps a host, VPN, route, or Docker network. Choose another subnet or use --allow-network-overlap after review."
fi

REVERSE_PROXY_IP=$(env_value EXPORTDOCMANAGER_REVERSE_PROXY_IP)
REVERSE_PROXY_IP=${REVERSE_PROXY_IP:-$(int_to_ipv4 $((SUBNET_START + 10)))}
PROXY_NUMBER=$(ipv4_to_int "$REVERSE_PROXY_IP") || fail "Existing reverse proxy IP is invalid."
((PROXY_NUMBER > SUBNET_START && PROXY_NUMBER < SUBNET_END)) || fail "Reverse proxy IP is outside the container subnet."

POSTGRES_DATABASE=$(env_value POSTGRES_DB)
POSTGRES_DATABASE=${POSTGRES_DATABASE:-exportdoc}
POSTGRES_ADMIN_USERNAME=$(env_value POSTGRES_USER)
POSTGRES_ADMIN_USERNAME=${POSTGRES_ADMIN_USERNAME:-exportdoc}
POSTGRES_APP_USERNAME=$(env_value EXPORTDOCMANAGER_POSTGRES_APP_USER)
POSTGRES_APP_USERNAME=${POSTGRES_APP_USERNAME:-exportdoc_app}
POSTGRES_OWNER_ROLE=$(env_value EXPORTDOCMANAGER_POSTGRES_OWNER_ROLE)
POSTGRES_OWNER_ROLE=${POSTGRES_OWNER_ROLE:-exportdoc_owner}
POSTGRES_MAINTENANCE_USERNAME=$(env_value EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_USER)
POSTGRES_MAINTENANCE_USERNAME=${POSTGRES_MAINTENANCE_USERNAME:-exportdoc_maintenance}
[[ "$POSTGRES_DATABASE" =~ ^[A-Za-z0-9_]{1,63}$ ]] || fail "POSTGRES_DB must contain 1-63 letters, digits, or underscores."
for role_name in "$POSTGRES_ADMIN_USERNAME" "$POSTGRES_APP_USERNAME" "$POSTGRES_OWNER_ROLE" "$POSTGRES_MAINTENANCE_USERNAME"; do
  [[ "$role_name" =~ ^[A-Za-z0-9_]{1,63}$ ]] || fail "PostgreSQL role names must contain 1-63 letters, digits, or underscores."
done
[[ "$POSTGRES_ADMIN_USERNAME" != "$POSTGRES_APP_USERNAME" &&
   "$POSTGRES_ADMIN_USERNAME" != "$POSTGRES_OWNER_ROLE" &&
   "$POSTGRES_ADMIN_USERNAME" != "$POSTGRES_MAINTENANCE_USERNAME" &&
   "$POSTGRES_APP_USERNAME" != "$POSTGRES_OWNER_ROLE" &&
   "$POSTGRES_APP_USERNAME" != "$POSTGRES_MAINTENANCE_USERNAME" &&
   "$POSTGRES_OWNER_ROLE" != "$POSTGRES_MAINTENANCE_USERNAME" ]] ||
  fail "PostgreSQL administrator, owner, application, and maintenance roles must be distinct."

mkdir -p -- "$RUNTIME_ROOT"
assert_safe_directory_path "$RUNTIME_ROOT" "Runtime data root"
RUNTIME_ROOT=$(cd -- "$RUNTIME_ROOT" && pwd -P)
[[ "$RUNTIME_ROOT" != "/" ]] || fail "The resolved runtime data root must not be the filesystem root."
API_DATA_ROOT="$RUNTIME_ROOT/api-data"
CONFIG_ROOT="$API_DATA_ROOT/Config"
REPORT_PDF_ROOT="$API_DATA_ROOT/Cache/ReportPdf"
BROWSER_ROOT="$RUNTIME_ROOT/browser"
POSTGRES_ROOT="$RUNTIME_ROOT/postgres"
LETSENCRYPT_ROOT="$RUNTIME_ROOT/letsencrypt"
ACME_WEB_ROOT="$RUNTIME_ROOT/acme-webroot"
for runtime_directory in "$API_DATA_ROOT" "$CONFIG_ROOT" "$REPORT_PDF_ROOT" "$BROWSER_ROOT" "$POSTGRES_ROOT" "$LETSENCRYPT_ROOT" "$ACME_WEB_ROOT"; do
  assert_safe_directory_path "$runtime_directory" "Managed runtime directory"
done
mkdir -p -- "$CONFIG_ROOT" "$REPORT_PDF_ROOT" "$BROWSER_ROOT" "$POSTGRES_ROOT" "$LETSENCRYPT_ROOT" "$ACME_WEB_ROOT"
for runtime_directory in "$API_DATA_ROOT" "$CONFIG_ROOT" "$REPORT_PDF_ROOT" "$BROWSER_ROOT" "$POSTGRES_ROOT" "$LETSENCRYPT_ROOT" "$ACME_WEB_ROOT"; do
  assert_safe_directory_path "$runtime_directory" "Managed runtime directory"
done
SETTINGS_FILE="$CONFIG_ROOT/appsettings.json"
[[ ! -L "$SETTINGS_FILE" ]] || fail "Managed application settings must not be a symbolic link: $SETTINGS_FILE"
[[ ! -e "$SETTINGS_FILE" || -f "$SETTINGS_FILE" ]] || fail "Managed application settings must be a regular file: $SETTINGS_FILE"
# The API image runs as the fixed, unprivileged uid/gid 10001. PostgreSQL's
# Debian image runs as uid/gid 999. Bind-mount ownership is prepared up front
# so neither service needs a world-writable host directory.
chown -R 10001:10001 "$API_DATA_ROOT"
chown -R 10001:10001 "$BROWSER_ROOT"
chown -R 999:999 "$POSTGRES_ROOT"
chown root:root "$RUNTIME_ROOT" "$ACME_WEB_ROOT"
chown root:101 "$LETSENCRYPT_ROOT"
chmod 700 "$RUNTIME_ROOT"
chmod 0700 "$POSTGRES_ROOT"
chmod 0750 "$API_DATA_ROOT" "$CONFIG_ROOT"
chmod 0750 "$REPORT_PDF_ROOT" "$BROWSER_ROOT"
chmod 0750 "$LETSENCRYPT_ROOT"
chmod 0755 "$ACME_WEB_ROOT"
if [[ ! -f "$SETTINGS_FILE" ]]; then
  cat > "$SETTINGS_FILE" <<EOF
{
  "System": {
    "DatabaseProvider": "PostgreSQL",
    "SqliteDatabaseFileName": "data.db",
    "PostgreSqlHost": "postgres",
    "PostgreSqlPort": 5432,
    "PostgreSqlDatabase": "$POSTGRES_DATABASE",
    "PostgreSqlUsername": "$POSTGRES_APP_USERNAME",
    "PostgreSqlPassword": "",
    "PostgreSqlAdditionalOptions": "Pooling=true;Maximum Pool Size=100;Timeout=15;Command Timeout=60"
  }
}
EOF
fi

MASTER_KEY=$(env_value EXPORTDOCMANAGER_MASTER_KEY)
ALLOWED_ORIGINS=$(env_value EXPORTDOCMANAGER_ALLOWED_ORIGINS)
ADDITIONAL_TRUSTED_PROXIES=$(env_value EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES)
set_env_value POSTGRES_DB "$POSTGRES_DATABASE"
set_env_value POSTGRES_USER "$POSTGRES_ADMIN_USERNAME"
set_env_value POSTGRES_PASSWORD "$POSTGRES_PASSWORD"
set_env_value EXPORTDOCMANAGER_POSTGRES_APP_USER "$POSTGRES_APP_USERNAME"
set_env_value EXPORTDOCMANAGER_POSTGRES_APP_PASSWORD "$POSTGRES_APP_PASSWORD"
set_env_value EXPORTDOCMANAGER_POSTGRES_OWNER_ROLE "$POSTGRES_OWNER_ROLE"
set_env_value EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_USER "$POSTGRES_MAINTENANCE_USERNAME"
set_env_value EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD "$POSTGRES_MAINTENANCE_PASSWORD"
set_env_value EXPORTDOCMANAGER_BOOTSTRAP_TOKEN "$BOOTSTRAP_TOKEN"
set_env_value EXPORTDOCMANAGER_MASTER_KEY "$MASTER_KEY"
if [[ "$MODE" == "https" ]]; then
  set_env_value EXPORTDOCMANAGER_WEB_PORT 80
  set_env_value EXPORTDOCMANAGER_WEB_BIND_ADDRESS "$WEB_BIND_ADDRESS"
  set_env_value EXPORTDOCMANAGER_HTTPS_PORT 443
  set_env_value EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY false
else
  set_env_value EXPORTDOCMANAGER_WEB_PORT "$WEB_PORT"
  set_env_value EXPORTDOCMANAGER_WEB_BIND_ADDRESS "$WEB_BIND_ADDRESS"
  set_env_value EXPORTDOCMANAGER_HTTPS_PORT 8443
  if ((ALLOW_INSECURE_DISASTER_RECOVERY == 1)); then
    set_env_value EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY true
  else
    set_env_value EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY false
  fi
fi
set_env_value EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES "$ADDITIONAL_TRUSTED_PROXIES"
set_env_value EXPORTDOCMANAGER_TLS_CERTIFICATE ./secrets/tls/server.crt
set_env_value EXPORTDOCMANAGER_TLS_PRIVATE_KEY ./secrets/tls/server.key
set_env_value EXPORTDOCMANAGER_RUNTIME_ROOT "$RUNTIME_ROOT"
set_env_value EXPORTDOCMANAGER_ALLOWED_ORIGINS "$ALLOWED_ORIGINS"
set_env_value EXPORTDOCMANAGER_CONTAINER_SUBNET "$CONTAINER_SUBNET"
set_env_value EXPORTDOCMANAGER_REVERSE_PROXY_IP "$REVERSE_PROXY_IP"
set_env_value EXPORTDOCMANAGER_IMAGE_NAMESPACE "$IMAGE_NAMESPACE"
set_env_value EXPORTDOCMANAGER_IMAGE_TAG "$IMAGE_TAG"
set_env_value EXPORTDOCMANAGER_IMAGE_REVISION "$IMAGE_REVISION"
set_env_value EXPORTDOCMANAGER_DEPLOYMENT_MODE "$MODE"
set_env_value EXPORTDOCMANAGER_PUBLIC_DOMAIN "$PUBLIC_DOMAIN"
set_env_value EXPORTDOCMANAGER_ACME_EMAIL "$ACME_EMAIL"
set_env_value TZ Asia/Shanghai
chmod 600 "$ENVIRONMENT_FILE"
chown 10001:10001 "$SETTINGS_FILE"
chmod 0600 "$SETTINGS_FILE"

if [[ -n ${GHCR_TOKEN:-} ]]; then
  [[ -n ${GHCR_USER:-} ]] || fail "GHCR_USER is required when GHCR_TOKEN is set."
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io --username "$GHCR_USER" --password-stdin >/dev/null
fi

COMPOSE=(docker compose -f "$INSTALL_DIR/docker-compose.ghcr.yml")
if [[ "$MODE" == "https" ]]; then
  COMPOSE+=(-f "$INSTALL_DIR/docker-compose.acme.yml")
fi
COMPOSE+=(--env-file "$ENVIRONMENT_FILE")
"${COMPOSE[@]}" config --quiet

note "Deployment files prepared in $INSTALL_DIR"
note "Runtime data root: $RUNTIME_ROOT"
note "Image tag: $IMAGE_TAG"
note "Image revision: $IMAGE_REVISION"
note "Container subnet: $CONTAINER_SUBNET"
if ((NO_START == 1)); then
  note "--no-start selected; images were not pulled and containers were not changed."
  DEPLOYMENT_SUCCEEDED=1
  exit 0
fi

if ! "${COMPOSE[@]}" pull; then
  fail "Image pull failed. Confirm the tag and package visibility; private packages require GHCR_USER and GHCR_TOKEN."
fi

for component in api browser web; do
  image_reference="$IMAGE_NAMESPACE/export-doc-manager-$component:$IMAGE_TAG"
  actual_revision=$(docker image inspect "$image_reference" --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}')
  actual_revision=${actual_revision,,}
  [[ "$actual_revision" == "$IMAGE_REVISION" ]] ||
    fail "Image revision mismatch for $image_reference: expected $IMAGE_REVISION, received ${actual_revision:-<missing>}."
done

if [[ "$MODE" == "https" ]]; then
  CERTIFICATE_ROOT="$RUNTIME_ROOT/letsencrypt"
  CERTIFICATE_FILE="$CERTIFICATE_ROOT/live/exportdocmanager/fullchain.pem"
  CERTIFICATE_KEY="$CERTIFICATE_ROOT/live/exportdocmanager/privkey.pem"
  prepare_nginx_certificate_permissions() {
    local certificate_directory
    local certificate_path
    for certificate_directory in \
      "$CERTIFICATE_ROOT" \
      "$CERTIFICATE_ROOT/live" \
      "$CERTIFICATE_ROOT/live/exportdocmanager" \
      "$CERTIFICATE_ROOT/archive" \
      "$CERTIFICATE_ROOT/archive/exportdocmanager"; do
      [[ ! -d "$certificate_directory" ]] || {
        chown root:101 "$certificate_directory"
        chmod 0750 "$certificate_directory"
      }
    done
    for certificate_path in "$CERTIFICATE_ROOT/archive/exportdocmanager/"*.pem; do
      [[ ! -f "$certificate_path" ]] || {
        chown root:101 "$certificate_path"
        chmod 0640 "$certificate_path"
      }
    done
  }
  certificate_is_usable() {
    local certificate_public_key private_public_key
    [[ -f "$CERTIFICATE_FILE" && -f "$CERTIFICATE_KEY" ]] || return 1
    openssl x509 -in "$CERTIFICATE_FILE" -noout -checkhost "$PUBLIC_DOMAIN" >/dev/null 2>&1 || return 1
    openssl x509 -in "$CERTIFICATE_FILE" -noout -checkend 2592000 >/dev/null 2>&1 || return 1
    certificate_public_key=$(
      openssl x509 -in "$CERTIFICATE_FILE" -pubkey -noout 2>/dev/null |
        openssl pkey -pubin -outform DER 2>/dev/null |
        openssl dgst -sha256 2>/dev/null
    ) || return 1
    private_public_key=$(
      openssl pkey -in "$CERTIFICATE_KEY" -pubout -outform DER 2>/dev/null |
        openssl dgst -sha256 2>/dev/null
    ) || return 1
    [[ -n "$certificate_public_key" && "$certificate_public_key" == "$private_public_key" ]]
  }

  CERTIFICATE_REQUIRED=1
  if certificate_is_usable; then
    CERTIFICATE_REQUIRED=0
  fi
  if ((CERTIFICATE_REQUIRED == 1)); then
    note "Requesting the initial Let's Encrypt certificate for $PUBLIC_DOMAIN..."
    "${COMPOSE[@]}" stop web certbot >/dev/null 2>&1 || true
    CERTIFICATE_BACKUP=$(mktemp -d "$RUNTIME_ROOT/.letsencrypt.previous.XXXXXX")
    cp -a -- "$CERTIFICATE_ROOT/." "$CERTIFICATE_BACKUP/"
    CERTIFICATE_STATE_CAPTURED=1
    if ! docker run --rm --name exportdocmanager-certbot-bootstrap \
        --publish 80:80 \
        --volume "$CERTIFICATE_ROOT:/etc/letsencrypt" \
        certbot/certbot:v5.7.0 certonly \
        --standalone \
        --non-interactive \
        --agree-tos \
        --no-eff-email \
        --preferred-challenges http \
        --force-renewal \
        --cert-name exportdocmanager \
        --domain "$PUBLIC_DOMAIN" \
        --email "$ACME_EMAIL"; then
      fail "Certificate request failed. Check DNS and inbound TCP 80; the installer will restore the previous deployment when possible."
    fi
  fi
  prepare_nginx_certificate_permissions
fi

if ! "${COMPOSE[@]}" up -d --remove-orphans; then
  "${COMPOSE[@]}" ps --all >&2 || true
  DIAGNOSTIC_SERVICES=(postgres browser api web)
  [[ "$MODE" == "https" ]] && DIAGNOSTIC_SERVICES+=(certbot)
  "${COMPOSE[@]}" logs --no-color --tail=120 "${DIAGNOSTIC_SERVICES[@]}" >&2 || true
  fail "Container startup failed. Review the service logs above; existing runtime data was not deleted."
fi

if [[ "$MODE" == "https" ]]; then
  READINESS_URL="https://${PUBLIC_DOMAIN}/readyz"
  ACCESS_URL="https://${PUBLIC_DOMAIN}"
  READINESS_ADDRESS=$WEB_BIND_ADDRESS
  [[ "$READINESS_ADDRESS" != "0.0.0.0" ]] || READINESS_ADDRESS=127.0.0.1
  READINESS_ARGUMENTS=(--resolve "${PUBLIC_DOMAIN}:443:${READINESS_ADDRESS}")
else
  READINESS_ADDRESS=$WEB_BIND_ADDRESS
  if [[ "$READINESS_ADDRESS" == "0.0.0.0" ]]; then
    READINESS_ADDRESS=127.0.0.1
    HOST_ADDRESS=$(hostname -I 2>/dev/null | awk '{print $1}')
    ACCESS_URL="http://${HOST_ADDRESS:-SERVER_IP}:${WEB_PORT}"
  else
    ACCESS_URL="http://${WEB_BIND_ADDRESS}:${WEB_PORT}"
  fi
  READINESS_URL="http://${READINESS_ADDRESS}:${WEB_PORT}/readyz"
  READINESS_ARGUMENTS=()
fi

READY=0
for attempt in {1..120}; do
  if curl "${READINESS_ARGUMENTS[@]}" --connect-timeout 3 --max-time 5 --fail --silent --show-error "$READINESS_URL" >/dev/null 2>&1; then
    READY=1
    break
  fi
  sleep 3
done
if ((READY != 1)); then
  "${COMPOSE[@]}" ps --all >&2 || true
  DIAGNOSTIC_SERVICES=(postgres browser api web)
  [[ "$MODE" == "https" ]] && DIAGNOSTIC_SERVICES+=(certbot)
  "${COMPOSE[@]}" logs --no-color --tail=120 "${DIAGNOSTIC_SERVICES[@]}" >&2 || true
  if [[ "$MODE" == "https" ]]; then
    fail "HTTPS did not become ready. Confirm DNS points to this VPS and inbound TCP 80/443 are open."
  fi
  fail "HTTP did not become ready. Review the container logs above."
fi

note ""
note "ExportDocManager is ready: $ACCESS_URL"
ACTIVATION_MARKER="$INSTALL_DIR/.activation-token-presented"
[[ ! -L "$ACTIVATION_MARKER" ]] || fail "Activation marker must not be a symbolic link: $ACTIVATION_MARKER"
[[ ! -e "$ACTIVATION_MARKER" || -f "$ACTIVATION_MARKER" ]] || fail "Activation marker must be a regular file: $ACTIVATION_MARKER"
if [[ ! -f "$ACTIVATION_MARKER" ]]; then
  note "First activation token: $BOOTSTRAP_TOKEN"
  note "Open Advanced connection options, enter this token, and sign in as admin with the application password you want to set."
  ACTIVATION_FILE=$(mktemp "$INSTALL_DIR/.activation-token-presented.activate.XXXXXX")
  chmod 600 "$ACTIVATION_FILE"
  mv -f -- "$ACTIVATION_FILE" "$ACTIVATION_MARKER"
  ACTIVATION_FILE=""
else
  note "Existing database password, activation token, configuration, and runtime data were preserved."
fi
note "Status: cd $INSTALL_DIR && sudo ${COMPOSE[*]} ps"
note "The API port 5188 and PostgreSQL port are not published to the host."
DEPLOYMENT_SUCCEEDED=1
