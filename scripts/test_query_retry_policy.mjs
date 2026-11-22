import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = path.join(repoRoot, "apps/export-doc-web");
const require = createRequire(path.join(webRoot, "package.json"));
const { outputFiles } = await require("esbuild").build({
  stdin: { contents: `export * from './src/api/queryRetryPolicy.ts'; export * from './src/api/queryPollingPolicy.ts'; export { ApiError } from './src/api/index.ts'; export { QueryClient, QueryObserver, timeoutManager } from '@tanstack/react-query';`, resolveDir: webRoot },
  write: false, bundle: true, format: "esm", platform: "node", logLevel: "silent",
});
const previousWindow = globalThis.window;
globalThis.window = {}; // Exercise the browser observer's actual timer lifecycle.
const model = await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString("base64")}`);
const { ApiError, queryRetryDelay, shouldRetryQueryFailure } = model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };

for (const status of [408, 425, 429, 500, 502, 503, 504]) {
  assert(shouldRetryQueryFailure(0, new ApiError(status, "Transient", "")), `${status} should retry`);
}
for (const status of [400, 401, 403, 404, 409, 422]) {
  assert(!shouldRetryQueryFailure(0, new ApiError(status, "Business", "")), `${status} must not retry`);
}
assert(shouldRetryQueryFailure(0, new TypeError("fetch failed")), "network TypeError should retry");
assert(shouldRetryQueryFailure(0, new DOMException("deadline", "TimeoutError")), "transport deadlines should retry");
assert(!shouldRetryQueryFailure(0, new DOMException("cancelled", "AbortError")), "caller cancellation must not retry");
assert(!shouldRetryQueryFailure(2, new DOMException("deadline", "TimeoutError")), "deadline retries must remain bounded");
assert(!shouldRetryQueryFailure(0, new Error("client model failed")), "ordinary client error must not retry");
assert(!shouldRetryQueryFailure(2, new TypeError("fetch failed")), "retry count must be bounded");
assert(queryRetryDelay(0) === 750 && queryRetryDelay(1) === 1500 && queryRetryDelay(5) === 3000, "retry delay should use bounded backoff");
const { queryPollingInterval, oaRequestPollingInterval } = model;
const polling = (error = null, fetchFailureCount = 0, status = "Pending") => ({ state: { error, fetchFailureCount, data: { status } } });
assert(queryPollingInterval(polling()) === 30000, "healthy queries poll at 30 seconds");
for (const status of [400, 401, 403, 404, 409, 422]) assert(queryPollingInterval(polling(new ApiError(status, "", ""))) === false, "permanent errors stop polling");
assert(queryPollingInterval(polling(new TypeError("network"), 3)) === 240000, "transient failures back off after retries");
assert(queryPollingInterval(polling(new ApiError(503, "", ""), 10)) === 300000, "polling backoff is capped");
for (const status of ["Completed", "Cancelled", "Rejected", "Terminated", "HandedOff"]) assert(oaRequestPollingInterval(polling(null, 0, status)) === false, `${status} is terminal`);
assert(oaRequestPollingInterval(polling(null, 0, "Approved")) === 30000, "approved requests still await fulfillment");
assert(queryPollingInterval(polling(), 2000) === 2000, "active jobs retain their normal cadence");
assert(queryPollingInterval(polling(), false) === false, "idle lists do not poll");
assert(queryPollingInterval(polling(new TypeError("offline")), false) === 60000, "a failed initial load can recover without cached data");

const { QueryClient, QueryObserver, timeoutManager } = model;
const timers = new Map();
let nextId = 0, now = 0;
const schedule = (callback, delay, interval = false) => {
  const id = ++nextId;
  timers.set(id, { callback, at: now + delay, delay, interval });
  return id;
};
timeoutManager.setTimeoutProvider({
  setTimeout: (callback, delay) => schedule(callback, delay),
  clearTimeout: id => timers.delete(id),
  setInterval: (callback, delay) => schedule(callback, delay, true),
  clearInterval: id => timers.delete(id),
});
const settle = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
const advance = async elapsed => {
  now += elapsed;
  for (const [id, timer] of [...timers]) {
    if (timer.at > now || !timers.has(id)) continue;
    if (timer.interval) timer.at = now + timer.delay;
    else timers.delete(id);
    timer.callback();
    await settle();
  }
};
const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
try {
  let calls = 0;
  const observer = new QueryObserver(client, {
    queryKey: ["recover-job"],
    queryFn: async () => { if (++calls === 1) throw new ApiError(503, "temporary", ""); return { status: calls === 2 ? "Running" : "Succeeded" }; },
    refetchInterval: query => query.state.data?.status === "Succeeded" ? false : queryPollingInterval(query, 2000),
    refetchIntervalInBackground: false,
  });
  const unsubscribe = observer.subscribe(() => {});
  await settle();
  assert(calls === 1 && observer.getCurrentResult().isError, "initial failure is observable");
  await advance(2000);
  assert(calls === 1, "transient failures must not hammer the server at job cadence");
  await advance(58000);
  assert(calls === 2 && observer.getCurrentResult().data.status === "Running", "the observer recovers after exhausted request retries");
  await advance(2000);
  assert(calls === 3 && observer.getCurrentResult().data.status === "Succeeded", "recovery resumes active polling");
  await advance(300000);
  assert(calls === 3, "completed jobs stop polling");
  unsubscribe();

  let deniedCalls = 0;
  const denied = new QueryObserver(client, {
    queryKey: ["denied"], queryFn: async () => { deniedCalls++; throw new ApiError(403, "denied", ""); },
    refetchInterval: query => queryPollingInterval(query, 15000),
  });
  const stop = denied.subscribe(() => {});
  await settle(); await advance(300000);
  assert(deniedCalls === 1, "permission failures stop the actual observer timer");
  stop();
  assert(timers.size === 0, "unmounting releases polling timers");
} finally {
  client.clear();
  globalThis.window = previousWindow;
}
process.stdout.write("query retry policy tests passed\n");
