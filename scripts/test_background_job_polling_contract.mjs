import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..");
const require = createRequire(path.join(repoRoot, "apps/export-doc-web/package.json"));
const esbuild = require("esbuild");
const { outputFiles } = await esbuild.build({
  entryPoints: [path.join(repoRoot, "apps/export-doc-web/src/ui/downloadJobResult.ts")],
  bundle: true, write: false, format: "esm", platform: "node", logLevel: "silent",
});
const { waitForJobCompletion } = await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString("base64")}`);
const originalWindow = globalThis.window;
const originalDocument = globalThis.document;
const document = new EventTarget();
document.hidden = false;
globalThis.window = globalThis;
globalThis.document = document;
const running = { jobId: "job", status: "running" };
const succeeded = { ...running, status: "succeeded" };
try {
  let requests = 0;
  const client = { getJob: async () => { requests += 1; return succeeded; } };
  await assert.rejects(
    waitForJobCompletion(client, running, { timeoutMs: 5, timeoutMessage: "still running" }),
    /still running/,
    "a polling delay must not issue another request after the deadline",
  );
  assert.equal(requests, 0);

  const cancellation = new AbortController();
  cancellation.abort();
  await assert.rejects(waitForJobCompletion(client, succeeded, { signal: cancellation.signal }), { name: "AbortError" });
  assert.equal(await waitForJobCompletion(client, succeeded), succeeded);
  await assert.rejects(waitForJobCompletion(client, { ...running, status: "failed", errorMessage: "worker failed" }), /worker failed/);

  document.hidden = true;
  await assert.rejects(waitForJobCompletion(client, running, { timeoutMs: 5 }), /后台任务/);
  assert.equal(requests, 0, "hidden tabs must not poll");
  const hiddenCancellation = new AbortController();
  const hiddenWait = waitForJobCompletion(client, running, { signal: hiddenCancellation.signal });
  hiddenCancellation.abort();
  await assert.rejects(hiddenWait, { name: "AbortError" });
  document.hidden = false;

  let requestTimeout;
  const boundedClient = {
    getJob: async (_request, init) => {
      requestTimeout = init.timeoutMs;
      return succeeded;
    },
  };
  assert.equal(await waitForJobCompletion(boundedClient, running, { timeoutMs: 60_000 }), succeeded);
  assert(requestTimeout > 0 && requestTimeout < 60_000, "the HTTP request must use only the remaining wait budget");
} finally {
  globalThis.window = originalWindow;
  globalThis.document = originalDocument;
}
process.stdout.write("background job polling contracts passed\n");
