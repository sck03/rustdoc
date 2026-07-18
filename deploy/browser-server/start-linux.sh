#!/usr/bin/env sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
CONFIG="$ROOT/appsettings.json"
if [ ! -f "$CONFIG" ]; then
  echo "appsettings.json was not found: $CONFIG" >&2
  exit 1
fi
if grep -q 'CHANGE_ME_BEFORE_START' "$CONFIG"; then
  echo "请先编辑 appsettings.json，填写 PostgreSQL 地址、账号和密码。" >&2
  exit 1
fi

BROWSER=$(find "$ROOT/Browsers" -type f \( -name chrome-headless-shell -o -name chrome \) -print -quit 2>/dev/null || true)
if [ -z "$BROWSER" ]; then
  echo "内置 Chrome Headless Shell 不存在。" >&2
  exit 1
fi
chmod +x "$BROWSER" "$ROOT/ExportDocManager.Api"

export EXPORTDOCMANAGER_NETWORK_MODE=true
export EXPORTDOCMANAGER_PRODUCT_EDITION=Full
export EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE="$BROWSER"
exec "$ROOT/ExportDocManager.Api" \
  --app-root "$ROOT" \
  --data-root "$ROOT/App_Data" \
  --urls "${EXPORTDOCMANAGER_URLS:-http://0.0.0.0:5188}" \
  --network-mode true
