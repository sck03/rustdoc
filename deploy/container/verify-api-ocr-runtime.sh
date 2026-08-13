#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_root="$(cd -- "$script_directory/../.." && pwd -P)"
evidence_root="$repository_root/artifacts/container-runtime/evidence"
ocr_log="$evidence_root/api-ocr-runtime.log"
container_name="exportdoc-api-ocr-validation"
mkdir -p "$evidence_root"

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
cleanup

image_id="$(docker image inspect export-doc-manager-api --format '{{.Id}}' 2>/dev/null || true)"
if [ -z "$image_id" ]; then
  echo "The API image was not produced by the Compose build." >&2
  exit 1
fi

# This is a database-free payload probe. Override the image's production
# network settings so startup validation cannot require PostgreSQL here.
if timeout --signal=TERM --kill-after=15s 240s \
  docker run --rm \
    --name "$container_name" \
    --network none \
    --read-only \
    --pids-limit 512 \
    --tmpfs /tmp:rw,noexec,nosuid,size=64m,mode=1777 \
    --tmpfs /runtime-data:rw,noexec,nosuid,size=256m,uid=10001,gid=10001,mode=0750 \
    --entrypoint dotnet \
    "$image_id" \
    ExportDocManager.Api.dll \
    --app-root /app \
    --data-root /runtime-data \
    --urls http://127.0.0.1:5188 \
    --network-mode false \
    --verify-ocr-runtime \
    2>&1 | tee "$ocr_log"; then
  exit 0
fi

diagnostic="$(tail -n 80 "$ocr_log" || true)"
diagnostic="${diagnostic//'%'/'%25'}"
diagnostic="${diagnostic//$'\r'/'%0D'}"
diagnostic="${diagnostic//$'\n'/'%0A'}"
echo "::error title=API OCR runtime validation failed::$diagnostic"
exit 1
