import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "query-retry-policy-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "api", "queryRetryPolicy.ts").replaceAll("\\", "/");
const apiPath = path.join(repoRoot, "apps", "export-doc-web", "src", "api", "index.ts").replaceAll("\\", "/");
const pollingPath = path.join(path.dirname(modelPath), "queryPollingPolicy.ts");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as polling from ${JSON.stringify(pollingPath)}; import { ApiError } from ${JSON.stringify(apiPath)}; globalThis.__model = { ...model, ...polling, ApiError };`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { ApiError, queryRetryDelay, shouldRetryQueryFailure } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };

for (const status of [408, 425, 429, 500, 502, 503, 504]) {
  assert(shouldRetryQueryFailure(0, new ApiError(status, "Transient", "")), `${status} should retry`);
}
for (const status of [400, 401, 403, 404, 409, 422]) {
  assert(!shouldRetryQueryFailure(0, new ApiError(status, "Business", "")), `${status} must not retry`);
}
assert(shouldRetryQueryFailure(0, new TypeError("fetch failed")), "network TypeError should retry");
assert(!shouldRetryQueryFailure(0, new Error("client model failed")), "ordinary client error must not retry");
assert(!shouldRetryQueryFailure(2, new TypeError("fetch failed")), "retry count must be bounded");
assert(queryRetryDelay(0) === 750 && queryRetryDelay(1) === 1500 && queryRetryDelay(5) === 3000, "retry delay should use bounded backoff");
const { queryPollingInterval, oaRequestPollingInterval } = globalThis.__model;
const polling = (error = null, fetchFailureCount = 0, status = "Pending") => ({ state: { error, fetchFailureCount, data: { status } } });
assert(queryPollingInterval(polling()) === 30000, "healthy queries poll at 30 seconds");
for (const status of [400, 401, 403, 404, 409, 422]) assert(queryPollingInterval(polling(new ApiError(status, "", ""))) === false, "permanent errors stop polling");
assert(queryPollingInterval(polling(new TypeError("network"), 3)) === 240000, "transient failures back off after retries");
assert(queryPollingInterval(polling(new ApiError(503, "", ""), 10)) === 300000, "polling backoff is capped");
for (const status of ["Completed", "Cancelled", "Rejected", "Terminated", "HandedOff"]) assert(oaRequestPollingInterval(polling(null, 0, status)) === false, `${status} is terminal`);
assert(oaRequestPollingInterval(polling(null, 0, "Approved")) === 30000, "approved requests still await fulfillment");
process.stdout.write("query retry policy tests passed\n");
