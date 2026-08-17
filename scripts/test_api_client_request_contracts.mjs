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
  const queryModelPath = path.join(
    repoRoot,
    "apps",
    "export-doc-web",
    "src",
    "features",
    "query",
    "queryModel.ts",
  ).replaceAll("\\", "/");
  fs.writeFileSync(entry, `import * as api from ${JSON.stringify(clientPath)}; import * as queryModel from ${JSON.stringify(queryModelPath)}; globalThis.__api = api; globalThis.__queryModel = queryModel;`, "utf8");
  const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
  await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
  await import(pathToFileURL(bundle).href);

  const { createExportDocManagerApiClient } = globalThis.__api;
  const queryModel = globalThis.__queryModel;
  const queryFilters = queryModel.toApiQueryFilters({
    startDate: "2026-06-01",
    endDate: "2026-06-30",
    customerId: "0",
    exporterId: "12",
    keyword: "  QUERY-001  ",
    invoiceType: "实际数据",
    transportMode: "BY SEA",
  });
  assert.equal(queryFilters.startDate, "2026-06-01");
  assert.equal(queryFilters.endDateExclusive, "2026-07-01");
  assert.equal(queryFilters.exporterId, 12);
  assert.equal(queryFilters.keyword, "QUERY-001");
  assert.equal(JSON.stringify(queryFilters).includes("T00:00:00"), false, "DateOnly request fields must not contain time text");
  assert.equal(queryModel.nextCalendarDate("2024-02-29"), "2024-03-01", "leap-day query ranges must advance correctly");
  assert.equal(queryModel.nextCalendarDate("0001-01-01"), "0001-01-02", "DateOnly years below 100 must not be shifted into the twentieth century");
  assert.throws(
    () => queryModel.nextCalendarDate("2026-02-29"),
    /查询日期无效/,
    "invalid calendar dates must fail instead of being normalized or sent",
  );
  assert.throws(
    () => queryModel.nextCalendarDate("9999-12-31"),
    /查询结束日期超出支持范围/,
    "an exclusive end date beyond the DateOnly range must fail explicitly",
  );
  assert.throws(
    () => queryModel.createDefaultQueryFilters("2026-02-29"),
    /服务端业务日期无效/,
    "invalid server business dates must fail before a query is sent",
  );
  assert.doesNotThrow(
    () => queryModel.createDefaultQueryFilters("0001-01-01"),
    "the DateOnly minimum value must remain valid",
  );
  assert.throws(
    () => queryModel.toApiQueryFilters({ ...queryFilters, startDate: "2026-13-01" }),
    /查询日期无效/,
    "invalid query dates must fail before an API request is built",
  );
  const recoveredFilters = queryModel.readStoredQueryFilters(
    { startDate: "2026-02-29", endDate: "2026-06-30" },
    queryModel.createDefaultQueryFilters("2026-06-15"),
  );
  assert.equal(recoveredFilters.startDate, "2026-06-01", "invalid stored dates must fall back to the current business month");
  assert.equal(recoveredFilters.endDate, "2026-06-30", "valid stored dates must remain unchanged");
  const storedFilters = queryModel.readStoredQueryFilters({
    keyword: "current",
    contractNo: "legacy-contract",
    styleName: "legacy-style",
  }, queryModel.createDefaultQueryFilters("2026-06-15"));
  assert.equal(storedFilters.keyword, "current", "unreleased legacy fields must not be migrated into the current query model");
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
