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
ALLOW_HTTP_DISASTER_RECOVERY=0
FORCE=0
START=0
GENERATED_BOOTSTRAP_TOKEN=0

usage() {
  cat >&2 <<'EOF'
用法：
  ./initialize-linux.sh [选项]

未传数据库密码时会从终端隐藏输入；未传首次部署令牌时会自动生成并仅显示一次。

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
  --allow-http-disaster-recovery
                              仅可信办公网/VPN：允许纯 HTTP 网页备份恢复与完整迁移
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
    --allow-http-disaster-recovery) ALLOW_HTTP_DISASTER_RECOVERY=1; shift ;;
    --force) FORCE=1; shift ;;
    --start) START=1; shift ;;
    -h|--help) usage ;;
    *) echo "未知参数：$1" >&2; usage ;;
  esac
done

read_secret_from_tty() {
  prompt=$1
  if [ ! -c /dev/tty ]; then
    echo "缺少可交互终端；请通过 --postgres-password 或 POSTGRES_PASSWORD 提供数据库密码。" >&2
    exit 1
  fi
  if ! command -v stty >/dev/null 2>&1; then
    echo "缺少 stty，无法安全隐藏数据库密码输入。" >&2
    exit 1
  fi
  old_tty=$(stty -g </dev/tty)
  trap 'stty "$old_tty" </dev/tty 2>/dev/null || true' 0 1 2 15
  printf '%s' "$prompt" >/dev/tty
  stty -echo </dev/tty
  IFS= read -r secret </dev/tty
  stty "$old_tty" </dev/tty
  trap - 0 1 2 15
  printf '\n' >/dev/tty
  printf '%s' "$secret"
}

generate_bootstrap_token() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
    return
  fi
  if command -v od >/dev/null 2>&1 && [ -r /dev/urandom ]; then
    od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
    return
  fi
  echo "缺少 openssl，且无法从 /dev/urandom 生成首次部署令牌。" >&2
  exit 1
}

contains_control_character() {
  LC_ALL=C printf '%s' "$1" | grep -q '[[:cntrl:]]'
}

assert_safe_directory_path() {
  path=$1
  label=$2
  case "$path" in
    /*) ;;
    *) echo "$label 必须使用绝对路径：$path" >&2; exit 1 ;;
  esac

  candidate=$path
  while :; do
    if [ -L "$candidate" ]; then
      echo "$label 不能经过符号链接：$candidate" >&2
      exit 1
    fi
    if [ -e "$candidate" ] && [ ! -d "$candidate" ]; then
      echo "$label 不能经过普通文件：$candidate" >&2
      exit 1
    fi
    [ "$candidate" = "/" ] && break
    parent=${candidate%/*}
    [ -n "$parent" ] || parent=/
    [ "$parent" = "$candidate" ] && break
    candidate=$parent
  done
}

if [ -z "$POSTGRES_PASSWORD" ]; then
  POSTGRES_PASSWORD=$(read_secret_from_tty '请输入 PostgreSQL 密码（输入不会回显）: ')
fi
if [ -z "$BOOTSTRAP_TOKEN" ]; then
  BOOTSTRAP_TOKEN=$(generate_bootstrap_token)
  GENERATED_BOOTSTRAP_TOKEN=1
fi

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
if [ "${#POSTGRES_PASSWORD}" -lt 12 ] || [ "${#POSTGRES_PASSWORD}" -gt 1024 ]; then
  echo "PostgreSQL 密码长度必须为 12-1024 位。" >&2
  exit 1
fi
if [ "${#BOOTSTRAP_TOKEN}" -lt 24 ] || [ "${#BOOTSTRAP_TOKEN}" -gt 512 ]; then
  echo "首次部署令牌长度必须为 24-512 位。" >&2
  exit 1
fi
if ! printf '%s' "$POSTGRES_PORT" | grep -Eq '^[0-9]+$' || [ "$POSTGRES_PORT" -lt 1 ] || [ "$POSTGRES_PORT" -gt 65535 ]; then
  echo "PostgreSQL 端口必须在 1-65535 之间。" >&2
  exit 1
fi
if contains_control_character "$POSTGRES_PASSWORD$BOOTSTRAP_TOKEN$URLS$DATA_ROOT$ALLOWED_ORIGINS$TRUSTED_PROXIES$MASTER_KEY"; then
  echo "配置不能包含控制字符。" >&2
  exit 1
fi
if [ -n "$TRUSTED_PROXIES" ] && ! printf '%s' "$TRUSTED_PROXIES" | grep -Eq '^[0-9A-Fa-f:.,; ]+$'; then
  echo "可信代理只能填写 IP 地址，并用逗号或分号分隔。" >&2
  exit 1
fi

case "$DATA_ROOT" in
  /*) ;;
  *) DATA_ROOT="$ROOT/$DATA_ROOT" ;;
esac
while [ "$DATA_ROOT" != "/" ] && [ "${DATA_ROOT%/}" != "$DATA_ROOT" ]; do
  DATA_ROOT=${DATA_ROOT%/}
done
if [ "$DATA_ROOT" = "/" ]; then
  echo "数据根不能直接使用文件系统根。" >&2
  exit 1
fi
assert_safe_directory_path "$DATA_ROOT" "数据根"
mkdir -p "$DATA_ROOT"
DATA_ROOT=$(CDPATH= cd -- "$DATA_ROOT" && pwd)
if [ "$DATA_ROOT" = "/" ]; then
  echo "数据根不能直接使用文件系统根。" >&2
  exit 1
fi
assert_safe_directory_path "$DATA_ROOT" "数据根"
SECURITY_ROOT="$DATA_ROOT/Security"
CONFIG_ROOT="$DATA_ROOT/Config"
assert_safe_directory_path "$SECURITY_ROOT" "安全配置目录"
assert_safe_directory_path "$CONFIG_ROOT" "应用配置目录"
mkdir -p "$SECURITY_ROOT" "$CONFIG_ROOT"
assert_safe_directory_path "$SECURITY_ROOT" "安全配置目录"
assert_safe_directory_path "$CONFIG_ROOT" "应用配置目录"
chmod 700 "$DATA_ROOT" "$SECURITY_ROOT" "$CONFIG_ROOT"
CONFIG_FILE="$CONFIG_ROOT/appsettings.json"
ENV_FILE="$SECURITY_ROOT/browser-server.env"
POINTER_FILE="$ROOT/browser-server.env.path"
for managed_file in "$CONFIG_FILE" "$ENV_FILE" "$POINTER_FILE"; do
  if [ -L "$managed_file" ] || { [ -e "$managed_file" ] && [ ! -f "$managed_file" ]; }; then
    echo "受管配置路径必须是普通文件且不能是符号链接：$managed_file" >&2
    exit 1
  fi
done
if [ "$FORCE" -ne 1 ] && [ -f "$CONFIG_FILE" ] && ! grep -q 'CHANGE_ME_BEFORE_START' "$CONFIG_FILE"; then
  echo "已存在有效 appsettings.json；如确认覆盖配置，请增加 --force。" >&2
  exit 1
fi
if [ "$FORCE" -ne 1 ] && [ -f "$ENV_FILE" ]; then
  echo "已存在 browser-server.env；如确认覆盖令牌/运行配置，请增加 --force。" >&2
  exit 1
fi

CONFIG_TEMP=$(mktemp "$CONFIG_FILE.tmp.XXXXXX")
ENV_TEMP=$(mktemp "$ENV_FILE.tmp.XXXXXX")
POINTER_TEMP=$(mktemp "$POINTER_FILE.tmp.XXXXXX")
cleanup_temporary_files() {
  for temporary_file in "$CONFIG_TEMP" "$ENV_TEMP" "$POINTER_TEMP"; do
    [ -z "$temporary_file" ] || rm -f -- "$temporary_file"
  done
}
trap cleanup_temporary_files 0
trap 'cleanup_temporary_files; exit 1' 1 2 15

cat > "$CONFIG_TEMP" <<EOF
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
chmod 600 "$CONFIG_TEMP"

ALLOW_HTTP_VALUE=false
if [ "$ALLOW_HTTP_DISASTER_RECOVERY" -eq 1 ]; then
  ALLOW_HTTP_VALUE=true
fi
{
  printf 'EXPORTDOCMANAGER_POSTGRES_PASSWORD=%s\n' "$POSTGRES_PASSWORD"
  printf 'EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=%s\n' "$BOOTSTRAP_TOKEN"
  printf 'EXPORTDOCMANAGER_URLS=%s\n' "$URLS"
  printf 'EXPORTDOCMANAGER_DATA_ROOT=%s\n' "$DATA_ROOT"
  printf 'EXPORTDOCMANAGER_ALLOWED_ORIGINS=%s\n' "$ALLOWED_ORIGINS"
  printf 'EXPORTDOCMANAGER_TRUSTED_PROXIES=%s\n' "$TRUSTED_PROXIES"
  printf 'EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=%s\n' "$ALLOW_HTTP_VALUE"
  printf 'EXPORTDOCMANAGER_NETWORK_MODE=true\n'
  printf 'EXPORTDOCMANAGER_PRODUCT_EDITION=Full\n'
  if [ -n "$MASTER_KEY" ]; then printf 'EXPORTDOCMANAGER_MASTER_KEY=%s\n' "$MASTER_KEY"; fi
} > "$ENV_TEMP"
chmod 600 "$ENV_TEMP"
printf '%s\n' "$ENV_FILE" > "$POINTER_TEMP"
chmod 600 "$POINTER_TEMP"

mv -f -- "$CONFIG_TEMP" "$CONFIG_FILE"
CONFIG_TEMP=""
mv -f -- "$ENV_TEMP" "$ENV_FILE"
ENV_TEMP=""
mv -f -- "$POINTER_TEMP" "$POINTER_FILE"
POINTER_TEMP=""

echo "浏览器服务器配置已完成。"
echo "数据库配置: $CONFIG_FILE"
echo "运行环境（含数据库密码和首次部署令牌）: $ENV_FILE"
echo "数据根: $DATA_ROOT"
echo "监听: $URLS"
if [ "$ALLOW_HTTP_DISASTER_RECOVERY" -eq 1 ]; then
  echo "警告：已允许纯 HTTP 网页备份恢复和完整迁移，只应在受防火墙保护的可信办公网/VPN 使用。" >&2
fi
if [ "$GENERATED_BOOTSTRAP_TOKEN" -eq 1 ]; then
  echo "首次部署令牌（仅显示这一次）: $BOOTSTRAP_TOKEN"
fi
echo "该脚本不会安装 PostgreSQL、修改防火墙或注册 systemd 服务。"

if [ "$START" -eq 1 ]; then
  exec "$ROOT/start-linux.sh"
fi
