#!/usr/bin/env sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
RUNTIME_ENV="$ROOT/App_Data/Security/browser-server.env"
RUNTIME_ENV_POINTER="$ROOT/browser-server.env.path"
if [ -f "$RUNTIME_ENV_POINTER" ]; then
  configured_path=$(sed -n '1p' "$RUNTIME_ENV_POINTER")
  if [ -n "$configured_path" ]; then
    case "$configured_path" in
      /*) RUNTIME_ENV=$configured_path ;;
      *) RUNTIME_ENV="$ROOT/$configured_path" ;;
    esac
  fi
fi
if [ -f "$RUNTIME_ENV" ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
      ''|'#'*) continue ;;
    esac
    key=${line%%=*}
    value=${line#*=}
    case "$key" in
      EXPORTDOCMANAGER_*|POSTGRES_*) export "$key=$value" ;;
    esac
  done < "$RUNTIME_ENV"
fi

DATA_ROOT=${EXPORTDOCMANAGER_DATA_ROOT:-$ROOT/App_Data}
case "$DATA_ROOT" in
  /*) ;;
  *) DATA_ROOT="$ROOT/$DATA_ROOT" ;;
esac
mkdir -p "$DATA_ROOT"
DATA_ROOT=$(CDPATH= cd -- "$DATA_ROOT" && pwd)
CONFIG="$DATA_ROOT/Config/appsettings.json"

if [ ! -f "$CONFIG" ]; then
  echo "appsettings.json was not found: $CONFIG" >&2
  exit 1
fi
if grep -q 'CHANGE_ME_BEFORE_START' "$CONFIG"; then
  echo "请先编辑 appsettings.json，填写 PostgreSQL 地址、账号和密码。" >&2
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
case "$(uname -m)" in
  aarch64|arm64) DEFAULT_OCR_RUNTIME=disabled ;;
  *) DEFAULT_OCR_RUNTIME=enabled ;;
esac
export EXPORTDOCMANAGER_OCR_RUNTIME="${EXPORTDOCMANAGER_OCR_RUNTIME:-$DEFAULT_OCR_RUNTIME}"
case "$EXPORTDOCMANAGER_OCR_RUNTIME" in
  0|false|disabled|off|none|unsupported) ;;
  *)
    "$ROOT/ExportDocManager.Api" \
      --app-root "$ROOT" \
      --data-root "$DATA_ROOT" \
      --verify-ocr-runtime
    ;;
esac
exec "$ROOT/ExportDocManager.Api" \
  --app-root "$ROOT" \
  --data-root "$DATA_ROOT" \
  --urls "${EXPORTDOCMANAGER_URLS:-http://0.0.0.0:5188}" \
  --network-mode true
