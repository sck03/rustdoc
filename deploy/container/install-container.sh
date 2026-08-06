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
WEB_PORT=""
PUBLIC_DOMAIN=""
ACME_EMAIL=""
REPOSITORY_REF="main"
CONTAINER_SUBNET=""
ALLOW_NETWORK_OVERLAP=0
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
  --image-namespace VALUE    Image namespace (default: ghcr.io/sck03)
  --install-dir PATH         Deployment and runtime root (default: /opt/export-doc-manager)
  --web-port PORT            Internal HTTP host port (default: 8080)
  --domain DOMAIN            Public DNS name for HTTPS mode
  --email EMAIL              ACME expiry notice email for HTTPS mode
  --repo-ref REF             Git ref used to download deployment assets (default: main)
  --subnet CIDR              Explicit private /24 to /28 Docker subnet
  --allow-network-overlap    Accept an explicitly configured overlapping subnet
  --no-start                 Generate and validate files without pulling or starting containers
  -h, --help                 Show this help

For private GHCR packages, export GHCR_USER and GHCR_TOKEN before running.
Optional first-install secrets can be supplied through
EXPORTDOCMANAGER_INSTALL_POSTGRES_PASSWORD and EXPORTDOCMANAGER_INSTALL_BOOTSTRAP_TOKEN.
EOF
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

note() {
  printf '%s\n' "$*"
}

while (($# > 0)); do
  case "$1" in
    --mode) MODE=${2:?Missing value for --mode}; shift 2 ;;
    --tag) IMAGE_TAG=${2:?Missing value for --tag}; shift 2 ;;
    --image-namespace) IMAGE_NAMESPACE=${2:?Missing value for --image-namespace}; shift 2 ;;
    --install-dir) INSTALL_DIR=${2:?Missing value for --install-dir}; shift 2 ;;
    --web-port) WEB_PORT=${2:?Missing value for --web-port}; shift 2 ;;
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
if command -v flock >/dev/null 2>&1; then
  exec 9>"$INSTALL_DIR/.install.lock"
  flock -n 9 || fail "Another installer process is using $INSTALL_DIR."
fi

ASSET_BASE=${EXPORTDOCMANAGER_DEPLOYMENT_ASSET_BASE:-"https://raw.githubusercontent.com/sck03/rustdoc/${REPOSITORY_REF}/deploy/container"}
download_asset() {
  local name=$1
  local destination="$INSTALL_DIR/$name"
  local temporary="$destination.download.$$"
  curl --fail --silent --show-error --location --retry 3 "$ASSET_BASE/$name" --output "$temporary"
  chmod 600 "$temporary"
  mv -f -- "$temporary" "$destination"
}

for asset in docker-compose.ghcr.yml docker-compose.acme.yml nginx.acme.conf install-container.sh; do
  download_asset "$asset"
done
chmod 700 "$INSTALL_DIR/install-container.sh"

ENVIRONMENT_FILE="$INSTALL_DIR/.env"
if [[ ! -f "$ENVIRONMENT_FILE" ]]; then
  : > "$ENVIRONMENT_FILE"
  chmod 600 "$ENVIRONMENT_FILE"
fi
ENVIRONMENT_BACKUP=$(mktemp "$INSTALL_DIR/.env.previous.XXXXXX")
cp -- "$ENVIRONMENT_FILE" "$ENVIRONMENT_BACKUP"
chmod 600 "$ENVIRONMENT_BACKUP"
cleanup() {
  rm -f -- "$ENVIRONMENT_BACKUP"
}
trap cleanup EXIT

env_value() {
  local key=$1
  awk -F= -v key="$key" '$1 == key { value=substr($0, index($0, "=") + 1) } END { print value }' "$ENVIRONMENT_FILE"
}

set_env_value() {
  local key=$1
  local value=$2
  local temporary="$ENVIRONMENT_FILE.tmp.$$"
  awk -v key="$key" -v value="$value" '
    BEGIN { replaced=0 }
    index($0, key "=") == 1 {
      if (!replaced) print key "=" value
      replaced=1
      next
    }
    { print }
    END { if (!replaced) print key "=" value }
  ' "$ENVIRONMENT_FILE" > "$temporary"
  chmod 600 "$temporary"
  mv -f -- "$temporary" "$ENVIRONMENT_FILE"
}

EXISTING_MODE=$(env_value EXPORTDOCMANAGER_DEPLOYMENT_MODE)
MODE=${MODE:-$EXISTING_MODE}
MODE=${MODE:-http}
[[ "$MODE" == "http" || "$MODE" == "https" ]] || fail "--mode must be http or https."

IMAGE_NAMESPACE=${IMAGE_NAMESPACE:-$(env_value EXPORTDOCMANAGER_IMAGE_NAMESPACE)}
IMAGE_NAMESPACE=${IMAGE_NAMESPACE:-ghcr.io/sck03}
IMAGE_TAG=${IMAGE_TAG:-$(env_value EXPORTDOCMANAGER_IMAGE_TAG)}
[[ -n "$IMAGE_TAG" ]] || fail "First installation requires --tag with an exact published image version."
if [[ -z "$WEB_PORT" && "$MODE" == "http" && "$EXISTING_MODE" == "http" ]]; then
  EXISTING_WEB_PORT=$(env_value EXPORTDOCMANAGER_WEB_PORT)
  [[ "$EXISTING_WEB_PORT" =~ ^[0-9]+$ ]] && WEB_PORT=$EXISTING_WEB_PORT
fi
WEB_PORT=${WEB_PORT:-8080}
[[ "$WEB_PORT" =~ ^[0-9]+$ ]] && ((WEB_PORT >= 1 && WEB_PORT <= 65535)) ||
  fail "--web-port must be between 1 and 65535."
[[ "$IMAGE_NAMESPACE" =~ ^ghcr\.io/[a-z0-9][a-z0-9._-]*$ ]] ||
  fail "--image-namespace must look like ghcr.io/account."
[[ "$IMAGE_TAG" =~ ^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$ ]] || fail "Invalid container image tag."
[[ "$IMAGE_TAG" != "latest" ]] || fail "The mutable latest tag is not accepted; use an exact published image version."

RUNTIME_ROOT=$(env_value EXPORTDOCMANAGER_RUNTIME_ROOT)
RUNTIME_ROOT=${RUNTIME_ROOT:-$INSTALL_DIR/runtime}
[[ "$RUNTIME_ROOT" == /* && "$RUNTIME_ROOT" != "/" && ! "$RUNTIME_ROOT" =~ [[:space:]] ]] ||
  fail "Existing EXPORTDOCMANAGER_RUNTIME_ROOT must be an absolute non-root path without whitespace."
SETTINGS_FILE="$RUNTIME_ROOT/api-data/Config/appsettings.json"

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
BOOTSTRAP_TOKEN=$(env_value EXPORTDOCMANAGER_BOOTSTRAP_TOKEN)
BOOTSTRAP_TOKEN=${BOOTSTRAP_TOKEN:-${EXPORTDOCMANAGER_INSTALL_BOOTSTRAP_TOKEN:-}}
BOOTSTRAP_TOKEN=${BOOTSTRAP_TOKEN:-$(random_hex 32)}
(( ${#POSTGRES_PASSWORD} >= 12 )) && [[ "$POSTGRES_PASSWORD" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "PostgreSQL password is invalid or shorter than 12 characters."
(( ${#BOOTSTRAP_TOKEN} >= 24 && ${#BOOTSTRAP_TOKEN} <= 512 )) && [[ "$BOOTSTRAP_TOKEN" =~ ^[A-Za-z0-9._~!@%+=:-]+$ ]] ||
  fail "Bootstrap token must contain 24-512 safe characters."

ipv4_to_int() {
  local address=$1 a b c d
  IFS=. read -r a b c d <<< "$address"
  [[ -n ${d:-} && $a =~ ^[0-9]+$ && $b =~ ^[0-9]+$ && $c =~ ^[0-9]+$ && $d =~ ^[0-9]+$ ]] || return 1
  ((a <= 255 && b <= 255 && c <= 255 && d <= 255)) || return 1
  printf '%u\n' "$(((a << 24) + (b << 16) + (c << 8) + d))"
}

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
POSTGRES_USERNAME=$(env_value POSTGRES_USER)
POSTGRES_USERNAME=${POSTGRES_USERNAME:-exportdoc}
[[ "$POSTGRES_DATABASE" =~ ^[A-Za-z0-9_]{1,63}$ ]] || fail "POSTGRES_DB must contain 1-63 letters, digits, or underscores."
[[ "$POSTGRES_USERNAME" =~ ^[A-Za-z0-9_]{1,63}$ ]] || fail "POSTGRES_USER must contain 1-63 letters, digits, or underscores."

mkdir -p -- "$RUNTIME_ROOT/api-data/Config" "$RUNTIME_ROOT/postgres" "$RUNTIME_ROOT/letsencrypt" "$RUNTIME_ROOT/acme-webroot"
# The API image runs as the fixed, unprivileged uid/gid 10001. PostgreSQL's
# Debian image runs as uid/gid 999. Bind-mount ownership is prepared up front
# so neither service needs a world-writable host directory.
chown -R 10001:10001 "$RUNTIME_ROOT/api-data"
chown -R 999:999 "$RUNTIME_ROOT/postgres"
chown root:root "$RUNTIME_ROOT" "$RUNTIME_ROOT/letsencrypt" "$RUNTIME_ROOT/acme-webroot"
chmod 700 "$RUNTIME_ROOT"
chmod 0700 "$RUNTIME_ROOT/postgres"
chmod 0750 "$RUNTIME_ROOT/api-data" "$RUNTIME_ROOT/api-data/Config"
chmod 0700 "$RUNTIME_ROOT/letsencrypt"
chmod 0755 "$RUNTIME_ROOT/acme-webroot"
if [[ ! -f "$SETTINGS_FILE" ]]; then
  cat > "$SETTINGS_FILE" <<EOF
{
  "System": {
    "DatabaseProvider": "PostgreSQL",
    "SqliteDatabaseFileName": "data.db",
    "PostgreSqlHost": "postgres",
    "PostgreSqlPort": 5432,
    "PostgreSqlDatabase": "$POSTGRES_DATABASE",
    "PostgreSqlUsername": "$POSTGRES_USERNAME",
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
set_env_value POSTGRES_USER "$POSTGRES_USERNAME"
set_env_value POSTGRES_PASSWORD "$POSTGRES_PASSWORD"
set_env_value EXPORTDOCMANAGER_BOOTSTRAP_TOKEN "$BOOTSTRAP_TOKEN"
set_env_value EXPORTDOCMANAGER_MASTER_KEY "$MASTER_KEY"
if [[ "$MODE" == "https" ]]; then
  set_env_value EXPORTDOCMANAGER_WEB_PORT 80
  set_env_value EXPORTDOCMANAGER_HTTPS_PORT 443
else
  set_env_value EXPORTDOCMANAGER_WEB_PORT "$WEB_PORT"
  set_env_value EXPORTDOCMANAGER_HTTPS_PORT 8443
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

restore_previous_deployment() {
  [[ "$EXISTING_MODE" == "http" || "$EXISTING_MODE" == "https" ]] || return 0

  cp -- "$ENVIRONMENT_BACKUP" "$ENVIRONMENT_FILE"
  chmod 600 "$ENVIRONMENT_FILE"
  local previous_compose=(docker compose -f "$INSTALL_DIR/docker-compose.ghcr.yml")
  if [[ "$EXISTING_MODE" == "https" ]]; then
    previous_compose+=(-f "$INSTALL_DIR/docker-compose.acme.yml")
  fi
  previous_compose+=(--env-file "$ENVIRONMENT_FILE")
  if "${previous_compose[@]}" up -d --remove-orphans >/dev/null 2>&1; then
    note "The previous $EXISTING_MODE deployment was restored."
  else
    note "WARNING: The previous deployment could not be restarted automatically; inspect Docker Compose state in $INSTALL_DIR." >&2
  fi
}

note "Deployment files prepared in $INSTALL_DIR"
note "Runtime data root: $RUNTIME_ROOT"
note "Image tag: $IMAGE_TAG"
note "Container subnet: $CONTAINER_SUBNET"
if ((NO_START == 1)); then
  note "--no-start selected; images were not pulled and containers were not changed."
  exit 0
fi

if ! "${COMPOSE[@]}" pull; then
  fail "Image pull failed. Confirm the tag and package visibility; private packages require GHCR_USER and GHCR_TOKEN."
fi

if [[ "$MODE" == "https" ]]; then
  CERTIFICATE_ROOT="$RUNTIME_ROOT/letsencrypt"
  CERTIFICATE_FILE="$CERTIFICATE_ROOT/live/exportdocmanager/fullchain.pem"
  CERTIFICATE_KEY="$CERTIFICATE_ROOT/live/exportdocmanager/privkey.pem"
  CERTIFICATE_DOMAIN_FILE="$CERTIFICATE_ROOT/exportdocmanager-domain"
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
    printf '%s\n' "$PUBLIC_DOMAIN" > "$CERTIFICATE_DOMAIN_FILE"
    chmod 600 "$CERTIFICATE_DOMAIN_FILE"
  fi
  if ((CERTIFICATE_REQUIRED == 1)); then
    note "Requesting the initial Let's Encrypt certificate for $PUBLIC_DOMAIN..."
    "${COMPOSE[@]}" stop web certbot >/dev/null 2>&1 || true
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
      restore_previous_deployment
      fail "Certificate request failed. Check DNS and inbound TCP 80; an existing deployment was restored when possible."
    fi
    printf '%s\n' "$PUBLIC_DOMAIN" > "$CERTIFICATE_DOMAIN_FILE"
    chmod 600 "$CERTIFICATE_DOMAIN_FILE"
  fi
fi

if ! "${COMPOSE[@]}" up -d --remove-orphans; then
  "${COMPOSE[@]}" ps --all >&2 || true
  DIAGNOSTIC_SERVICES=(postgres api web)
  [[ "$MODE" == "https" ]] && DIAGNOSTIC_SERVICES+=(certbot)
  "${COMPOSE[@]}" logs --no-color --tail=120 "${DIAGNOSTIC_SERVICES[@]}" >&2 || true
  fail "Container startup failed. Review the service logs above; existing runtime data was not deleted."
fi

if [[ "$MODE" == "https" ]]; then
  READINESS_URL="https://${PUBLIC_DOMAIN}/readyz"
  ACCESS_URL="https://${PUBLIC_DOMAIN}"
  READINESS_ARGUMENTS=(--resolve "${PUBLIC_DOMAIN}:443:127.0.0.1")
else
  READINESS_URL="http://127.0.0.1:${WEB_PORT}/readyz"
  HOST_ADDRESS=$(hostname -I 2>/dev/null | awk '{print $1}')
  ACCESS_URL="http://${HOST_ADDRESS:-SERVER_IP}:${WEB_PORT}"
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
  DIAGNOSTIC_SERVICES=(postgres api web)
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
if [[ ! -f "$ACTIVATION_MARKER" ]]; then
  note "First activation token: $BOOTSTRAP_TOKEN"
  note "Open Advanced connection options, enter this token, and sign in as admin with the application password you want to set."
  : > "$ACTIVATION_MARKER"
  chmod 600 "$ACTIVATION_MARKER"
else
  note "Existing database password, activation token, configuration, and runtime data were preserved."
fi
note "Status: cd $INSTALL_DIR && sudo ${COMPOSE[*]} ps"
note "The API port 5188 and PostgreSQL port are not published to the host."
