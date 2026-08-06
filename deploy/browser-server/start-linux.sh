#!/usr/bin/env sh
set -eu
umask 077

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
carriage_return=$(printf '\r')

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

RUNTIME_ENV="$ROOT/App_Data/Security/browser-server.env"
RUNTIME_ENV_POINTER="$ROOT/browser-server.env.path"
if [ -L "$RUNTIME_ENV_POINTER" ] || { [ -e "$RUNTIME_ENV_POINTER" ] && [ ! -f "$RUNTIME_ENV_POINTER" ]; }; then
  echo "运行环境定位文件必须是普通文件且不能是符号链接：$RUNTIME_ENV_POINTER" >&2
  exit 1
fi
if [ -f "$RUNTIME_ENV_POINTER" ]; then
  configured_path=$(sed -n '1{s/\r$//;p;}' "$RUNTIME_ENV_POINTER")
  if [ -n "$configured_path" ]; then
    if contains_control_character "$configured_path"; then
      echo "运行环境定位文件不能包含控制字符。" >&2
      exit 1
    fi
    case "$configured_path" in
      /*) RUNTIME_ENV=$configured_path ;;
      *) RUNTIME_ENV="$ROOT/$configured_path" ;;
    esac
  fi
fi
if [ -L "$RUNTIME_ENV" ] || { [ -e "$RUNTIME_ENV" ] && [ ! -f "$RUNTIME_ENV" ]; }; then
  echo "运行环境文件必须是普通文件且不能是符号链接：$RUNTIME_ENV" >&2
  exit 1
fi
RUNTIME_ENV_DIRECTORY=${RUNTIME_ENV%/*}
[ -n "$RUNTIME_ENV_DIRECTORY" ] || RUNTIME_ENV_DIRECTORY=/
assert_safe_directory_path "$RUNTIME_ENV_DIRECTORY" "运行环境目录"
if [ -f "$RUNTIME_ENV" ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    line=${line%"$carriage_return"}
    if contains_control_character "$line"; then
      echo "运行环境文件不能包含控制字符。" >&2
      exit 1
    fi
    case "$line" in
      ''|'#'*) continue ;;
    esac
    key=${line%%=*}
    value=${line#*=}
    case "$key" in
      EXPORTDOCMANAGER_*|POSTGRES_*)
        case "$key" in
          *[!A-Za-z0-9_]*)
            echo "运行环境文件包含无效变量名：$key" >&2
            exit 1
            ;;
        esac
        export "$key=$value"
        ;;
    esac
  done < "$RUNTIME_ENV"
fi

DATA_ROOT=${EXPORTDOCMANAGER_DATA_ROOT:-$ROOT/App_Data}
if contains_control_character "$DATA_ROOT"; then
  echo "数据根不能包含控制字符。" >&2
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
CONFIG_ROOT="$DATA_ROOT/Config"
assert_safe_directory_path "$CONFIG_ROOT" "应用配置目录"
CONFIG="$CONFIG_ROOT/appsettings.json"

if [ -L "$CONFIG" ] || { [ -e "$CONFIG" ] && [ ! -f "$CONFIG" ]; }; then
  echo "appsettings.json 必须是普通文件且不能是符号链接：$CONFIG" >&2
  exit 1
fi
if [ ! -f "$CONFIG" ]; then
  echo "appsettings.json was not found: $CONFIG" >&2
  exit 1
fi
if grep -q 'CHANGE_ME_BEFORE_START' "$CONFIG"; then
  echo "请先运行 initialize-linux.sh 生成 PostgreSQL 连接配置。" >&2
  exit 1
fi
if [ -z "${EXPORTDOCMANAGER_POSTGRES_PASSWORD:-}" ] && [ -z "${EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE:-}" ]; then
  echo "请在权限受限的 browser-server.env 中设置 EXPORTDOCMANAGER_POSTGRES_PASSWORD 或 EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE。" >&2
  exit 1
fi
if [ -z "${EXPORTDOCMANAGER_BOOTSTRAP_TOKEN:-}" ] || [ "${#EXPORTDOCMANAGER_BOOTSTRAP_TOKEN}" -lt 24 ]; then
  echo "请先设置至少 24 个字符的 EXPORTDOCMANAGER_BOOTSTRAP_TOKEN，用于首次 PostgreSQL 管理员初始化。" >&2
  exit 1
fi

BROWSER=$(find "$ROOT/Browsers" -type f \( -name chrome-headless-shell -o -name chrome \) -print -quit 2>/dev/null || true)
if [ -z "$BROWSER" ]; then
  echo "内置 Chrome Headless Shell / Chromium ARM64 不存在。" >&2
  exit 1
fi
chmod +x "$BROWSER" "$ROOT/ExportDocManager.Api"

export EXPORTDOCMANAGER_NETWORK_MODE=true
export EXPORTDOCMANAGER_PRODUCT_EDITION=Full
export EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE="$BROWSER"
if [ -x "$ROOT/Tools/PostgreSQL/bin/pg_dump" ]; then
  export EXPORTDOCMANAGER_POSTGRES_BIN="$ROOT/Tools/PostgreSQL/bin"
  export LD_LIBRARY_PATH="$ROOT/Tools/PostgreSQL/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
case "$(uname -m)" in
  aarch64|arm64) DEFAULT_OCR_RUNTIME=disabled ;;
  *) DEFAULT_OCR_RUNTIME=enabled ;;
esac
export EXPORTDOCMANAGER_OCR_RUNTIME="${EXPORTDOCMANAGER_OCR_RUNTIME:-$DEFAULT_OCR_RUNTIME}"
case "$EXPORTDOCMANAGER_OCR_RUNTIME" in
  0|false|disabled|off|none|unsupported) ;;
  *)
    PACKAGE_VERSION=$(sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ROOT/version.json" 2>/dev/null | sed -n '1p')
    CACHE_OCR_VERIFICATION=1
    case "$PACKAGE_VERSION" in
      ''|*[!A-Za-z0-9._-]*) PACKAGE_VERSION=unknown; CACHE_OCR_VERIFICATION=0 ;;
    esac
    RUNTIME_ARCH=$(uname -m)
    case "$RUNTIME_ARCH" in
      ''|*[!A-Za-z0-9._-]*) RUNTIME_ARCH=unknown; CACHE_OCR_VERIFICATION=0 ;;
    esac
    CACHE_ROOT="$DATA_ROOT/Cache"
    VERIFICATION_ROOT="$CACHE_ROOT/RuntimeVerification"
    assert_safe_directory_path "$CACHE_ROOT" "运行缓存目录"
    assert_safe_directory_path "$VERIFICATION_ROOT" "运行时验证目录"
    OCR_MARKER="$VERIFICATION_ROOT/ocr-${PACKAGE_VERSION}-${RUNTIME_ARCH}.ok"
    if [ -L "$OCR_MARKER" ] || { [ -e "$OCR_MARKER" ] && [ ! -f "$OCR_MARKER" ]; }; then
      echo "OCR 验证标记必须是 DataRoot 下的普通文件：$OCR_MARKER" >&2
      exit 1
    fi
    if [ "$CACHE_OCR_VERIFICATION" -ne 1 ] || [ ! -s "$OCR_MARKER" ]; then
      "$ROOT/ExportDocManager.Api" \
        --app-root "$ROOT" \
        --data-root "$DATA_ROOT" \
        --verify-ocr-runtime
      if [ "$CACHE_OCR_VERIFICATION" -eq 1 ]; then
        mkdir -p "$VERIFICATION_ROOT"
        assert_safe_directory_path "$VERIFICATION_ROOT" "运行时验证目录"
        OCR_MARKER_TEMP=$(mktemp "$OCR_MARKER.tmp.XXXXXX")
        printf 'version=%s\narchitecture=%s\n' "$PACKAGE_VERSION" "$RUNTIME_ARCH" > "$OCR_MARKER_TEMP"
        chmod 600 "$OCR_MARKER_TEMP"
        mv -f "$OCR_MARKER_TEMP" "$OCR_MARKER"
      fi
    fi
    ;;
esac
EFFECTIVE_URLS=${EXPORTDOCMANAGER_URLS:-http://0.0.0.0:5188}
if contains_control_character "$EFFECTIVE_URLS"; then
  echo "监听地址不能包含控制字符。" >&2
  exit 1
fi
exec "$ROOT/ExportDocManager.Api" \
  --app-root "$ROOT" \
  --data-root "$DATA_ROOT" \
  --urls "$EFFECTIVE_URLS" \
  --network-mode true
