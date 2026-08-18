import assert from "node:assert/strict";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const moduleUrl = pathToFileURL(path.resolve(
  repoRoot,
  "apps/export-doc-web/src/api/businessDateRefreshModel.ts",
));
const source = await import(moduleUrl.href);

assert.equal(source.calculateBusinessDateRefreshDelay(undefined, 0), null);
assert.equal(source.calculateBusinessDateRefreshDelay("invalid", 0), 0);
assert.equal(source.calculateBusinessDateRefreshDelay("2026-06-01T16:00:00Z", Date.parse("2026-06-01T15:59:59Z")), 2_000);
assert.equal(source.calculateBusinessDateRefreshDelay("2026-06-01T16:00:00Z", Date.parse("2026-06-01T16:00:01Z")), 0);

process.stdout.write("business-date refresh model tests passed\n");
