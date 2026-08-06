#!/usr/bin/env sh
set -eu
umask 077

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
POSTGRES_HOST=${POSTGRES_HOST:-127.0.0.1}
POSTGRES_PORT=${POSTGRES_PORT:-5432}
POSTGRES_DB=${POSTGRES_DB:-exportdoc}
POSTGRES_USER=${POSTGRES_USER:-exportdoc}
POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-}
BOOTSTRAP_TOKEN=${EXPORTDOCMANAGER_BOOTSTRAP_TOKEN:-}
URLS=${EXPORTDOCMANAGER_URLS:-http://0.0.0.0:5188}
DATA_ROOT=${EXPORTDOCMANAGER_DATA_ROOT:-$ROOT/App_Data}
ALLOWED_ORIGINS=${EXPORTDOCMANAGER_ALLOWED_ORIGINS:-}
TRUSTED_PROXIES=${EXPORTDOCMANAGER_TRUSTED_PROXIES:-}
MASTER_KEY=${EXPORTDOCMANAGER_MASTER_KEY:-}
FORCE=0
START=0

usage() {
  cat >&2 <<'EOF'
用法：
  ./initialize-linux.sh --postgres-password <密码> --bootstrap-token <至少24位令牌> [选项]

选项：
  --postgres-host <主机>       默认 127.0.0.1
  --postgres-port <端口>       默认 5432
  --postgres-database <名称>   默认 exportdoc
  --postgres-user <名称>       默认 exportdoc
  --urls <地址>                默认 http://0.0.0.0:5188
  --data-root <目录>           默认包内 App_Data
  --allowed-origins <列表>     可选，逗号/分号分隔的 HTTP/HTTPS 来源
  --trusted-proxies <列表>     可选，逗号/分号分隔的代理 IP
  --master-key <密钥>          可选，32 字节 Base64 或 64 位十六进制
  --force                      覆盖已有有效配置和令牌
  --start                      配置完成后立即启动
EOF
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --postgres-host) POSTGRES_HOST=${2:?缺少 --postgres-host 的值}; shift 2 ;;
    --postgres-port) POSTGRES_PORT=${2:?缺少 --postgres-port 的值}; shift 2 ;;
    --postgres-database) POSTGRES_DB=${2:?缺少 --postgres-database 的值}; shift 2 ;;
    --postgres-user) POSTGRES_USER=${2:?缺少 --postgres-user 的值}; shift 2 ;;
    --postgres-password) POSTGRES_PASSWORD=${2:?缺少 --postgres-password 的值}; shift 2 ;;
    --bootstrap-token) BOOTSTRAP_TOKEN=${2:?缺少 --bootstrap-token 的值}; shift 2 ;;
    --urls) URLS=${2:?缺少 --urls 的值}; shift 2 ;;
    --data-root) DATA_ROOT=${2:?缺少 --data-root 的值}; shift 2 ;;
    --allowed-origins) ALLOWED_ORIGINS=${2:?缺少 --allowed-origins 的值}; shift 2 ;;
    --trusted-proxies) TRUSTED_PROXIES=${2:?缺少 --trusted-proxies 的值}; shift 2 ;;
    --master-key) MASTER_KEY=${2:?缺少 --master-key 的值}; shift 2 ;;
    --force) FORCE=1; shift ;;
    --start) START=1; shift ;;
    -h|--help) usage ;;
    *) echo "未知参数：$1" >&2; usage ;;
  esac
done

safe_value() {
  name=$1
  value=$2
  pattern=$3
  if [ -z "$value" ] || ! printf '%s' "$value" | grep -Eq "$pattern"; then
    echo "$name 包含不支持的字符或为空。" >&2
    exit 1
  fi
}

safe_value "PostgreSQL 主机" "$POSTGRES_HOST" '^[A-Za-z0-9._:-]+$'
safe_value "PostgreSQL 数据库名" "$POSTGRES_DB" '^[A-Za-z0-9_.-]+$'
safe_value "PostgreSQL 用户名" "$POSTGRES_USER" '^[A-Za-z0-9_.-]+$'
safe_value "PostgreSQL 密码" "$POSTGRES_PASSWORD" '^[A-Za-z0-9._~!@%+=:-]+$'
safe_value "首次部署令牌" "$BOOTSTRAP_TOKEN" '^[A-Za-z0-9._~!@%+=:-]{24,512}$'
if [ "${#POSTGRES_PASSWORD}" -lt 12 ]; then
  echo "PostgreSQL 密码至少需要 12 位。" >&2
  exit 1
fi
if ! printf '%s' "$POSTGRES_PORT" | grep -Eq '^[0-9]+$' || [ "$POSTGRES_PORT" -lt 1 ] || [ "$POSTGRES_PORT" -gt 65535 ]; then
  echo "PostgreSQL 端口必须在 1-65535 之间。" >&2
  exit 1
fi
line_feed='
'
carriage_return=$(printf '\r')
case "$URLS$DATA_ROOT$ALLOWED_ORIGINS$TRUSTED_PROXIES$MASTER_KEY" in
  *"$line_feed"*|*"$carriage_return"*) echo "配置不能包含换行。" >&2; exit 1 ;;
esac
if [ -n "$TRUSTED_PROXIES" ] && ! printf '%s' "$TRUSTED_PROXIES" | grep -Eq '^[0-9A-Fa-f:.,; ]+$'; then
  echo "可信代理只能填写 IP 地址，并用逗号或分号分隔。" >&2
  exit 1
fi

case "$DATA_ROOT" in
  /*) ;;
  *) DATA_ROOT="$ROOT/$DATA_ROOT" ;;
esac
mkdir -p "$DATA_ROOT"
DATA_ROOT=$(CDPATH= cd -- "$DATA_ROOT" && pwd)
mkdir -p "$DATA_ROOT/Security" "$DATA_ROOT/Config"
CONFIG_FILE="$DATA_ROOT/Config/appsettings.json"
if [ "$FORCE" -ne 1 ] && [ -f "$CONFIG_FILE" ] && ! grep -q 'CHANGE_ME_BEFORE_START' "$CONFIG_FILE"; then
  echo "已存在有效 appsettings.json；如确认覆盖配置，请增加 --force。" >&2
  exit 1
fi
if [ "$FORCE" -ne 1 ] && [ -f "$DATA_ROOT/Security/browser-server.env" ]; then
  echo "已存在 browser-server.env；如确认覆盖令牌/运行配置，请增加 --force。" >&2
  exit 1
fi

cat > "$CONFIG_FILE" <<EOF
{
  "System": {
    "DatabaseProvider": "PostgreSQL",
    "SqliteDatabaseFileName": "data.db",
    "PostgreSqlHost": "$POSTGRES_HOST",
    "PostgreSqlPort": $POSTGRES_PORT,
    "PostgreSqlDatabase": "$POSTGRES_DB",
    "PostgreSqlUsername": "$POSTGRES_USER",
    "PostgreSqlPassword": "",
    "PostgreSqlAdditionalOptions": "Pooling=true;Maximum Pool Size=100;Timeout=15;Command Timeout=60"
  }
}
EOF
chmod 600 "$CONFIG_FILE"

ENV_FILE="$DATA_ROOT/Security/browser-server.env"
{
  printf 'EXPORTDOCMANAGER_POSTGRES_PASSWORD=%s\n' "$POSTGRES_PASSWORD"
  printf 'EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=%s\n' "$BOOTSTRAP_TOKEN"
  printf 'EXPORTDOCMANAGER_URLS=%s\n' "$URLS"
  printf 'EXPORTDOCMANAGER_DATA_ROOT=%s\n' "$DATA_ROOT"
  printf 'EXPORTDOCMANAGER_ALLOWED_ORIGINS=%s\n' "$ALLOWED_ORIGINS"
  printf 'EXPORTDOCMANAGER_TRUSTED_PROXIES=%s\n' "$TRUSTED_PROXIES"
  printf 'EXPORTDOCMANAGER_NETWORK_MODE=true\n'
  printf 'EXPORTDOCMANAGER_PRODUCT_EDITION=Full\n'
  if [ -n "$MASTER_KEY" ]; then printf 'EXPORTDOCMANAGER_MASTER_KEY=%s\n' "$MASTER_KEY"; fi
} > "$ENV_FILE"
chmod 600 "$ENV_FILE"
printf '%s\n' "$ENV_FILE" > "$ROOT/browser-server.env.path"

echo "浏览器服务器配置已完成。"
echo "数据库配置: $CONFIG_FILE"
echo "运行环境（含数据库密码和首次部署令牌）: $ENV_FILE"
echo "数据根: $DATA_ROOT"
echo "监听: $URLS"
echo "该脚本不会安装 PostgreSQL、修改防火墙或注册 systemd 服务。"

if [ "$START" -eq 1 ]; then
  exec "$ROOT/start-linux.sh"
fi
