import assert from "node:assert/strict";
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "api-client-contract-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

try {
  const clientPath = path.join(
    repoRoot,
    "apps",
    "export-doc-web",
    "src",
    "api",
    "generated",
    "exportDocManagerApi.ts",
  ).replaceAll("\\", "/");
  fs.writeFileSync(entry, `import * as api from ${JSON.stringify(clientPath)}; globalThis.__api = api;`, "utf8");
  const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
  await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
  await import(pathToFileURL(bundle).href);

  const { createExportDocManagerApiClient } = globalThis.__api;
  const requests = [];
  let notifyRequestStarted;
  const fetchImpl = (_url, init) => new Promise((resolve, reject) => {
    requests.push(init);
    notifyRequestStarted?.();
    if (init.signal?.aborted) {
      reject(init.signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
      return;
    }
    init.signal?.addEventListener("abort", () => {
      reject(init.signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
    }, { once: true });
  });

  const client = createExportDocManagerApiClient({
    baseUrl: "https://api.example.test",
    defaultTimeoutMs: 20,
    fetch: fetchImpl,
  });

  await assert.rejects(client.getHealth(), { name: "AbortError" }, "default timeout must abort a hung request");
  assert.equal(requests.length, 1);
  assert.equal(requests[0].signal.aborted, true);

  const callerController = new AbortController();
  const callerRequestStarted = new Promise((resolve) => {
    notifyRequestStarted = resolve;
  });
  const callerRequest = client.getHealth({ signal: callerController.signal, timeoutMs: 0 });
  await callerRequestStarted;
  notifyRequestStarted = undefined;
  callerController.abort();
  await assert.rejects(callerRequest, { name: "AbortError" }, "caller cancellation must propagate when timeout is disabled");
  assert.equal(requests.length, 2);
  assert.equal(requests[1].signal.aborted, true);

  const timeoutController = new AbortController();
  const timeoutRequest = client.getHealth({ signal: timeoutController.signal, timeoutMs: 20 });
  await assert.rejects(timeoutRequest, { name: "AbortError" }, "internal timeout must remain active with a caller signal");
  assert.equal(timeoutController.signal.aborted, false, "internal timeout must not abort the caller's controller");

  const tokenClient = createExportDocManagerApiClient({
    baseUrl: "https://api.example.test",
    defaultTimeoutMs: 20,
    accessToken: () => new Promise(() => undefined),
    fetch: fetchImpl,
  });
  await assert.rejects(
    tokenClient.getHealth(),
    { name: "AbortError" },
    "request timeout must cover a token provider that never resolves",
  );
  assert.equal(requests.length, 3, "a stalled token provider must not start a fetch request");

  const tokenCallerController = new AbortController();
  const tokenCallerRequest = tokenClient.getHealth({ signal: tokenCallerController.signal, timeoutMs: 0 });
  tokenCallerController.abort();
  await assert.rejects(
    tokenCallerRequest,
    { name: "AbortError" },
    "caller cancellation must interrupt a token provider before fetch",
  );
  assert.equal(requests.length, 3, "caller cancellation during token resolution must not start a fetch request");

  console.log("API client timeout and cancellation contracts passed.");
} finally {
  fs.rmSync(workspace, { recursive: true, force: true });
}
