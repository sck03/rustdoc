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
const { waitForJobCompletion, downloadCompletedJobResult } = await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString("base64")}`);
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
  await assert.rejects(waitForJobCompletion(client, running, { timeoutMs: 5 }), /文件任务/);
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
  let clicks = 0, removals = 0;
  const anchor = { click() { clicks++; }, remove() { removals++; } };
  document.createElement = () => anchor;
  document.body = { appendChild() {} };
  const ticketCancellation = new AbortController();
  const downloadClient = { resolveUrl: value => value, createJobDownloadTicket: async () => ({ downloadUrl: '/ticket/result' }) };
  const canceledDownload = downloadCompletedJobResult(downloadClient, succeeded, 'report.pdf', ticketCancellation.signal);
  ticketCancellation.abort();
  await assert.rejects(canceledDownload, { name: 'AbortError' });
  assert.equal(clicks, 0, 'a late download ticket must not start a download after cancellation');
  await downloadCompletedJobResult(downloadClient, succeeded, ' report.pdf ');
  assert.equal(clicks, 1); assert.equal(removals, 1); assert.equal(anchor.download, 'report.pdf');
  anchor.click = () => { throw new Error('browser refused download'); };
  await assert.rejects(downloadCompletedJobResult(downloadClient, succeeded), /browser refused download/);
  assert.equal(removals, 2, 'a failed click still removes its temporary link');
} finally {
  globalThis.window = originalWindow;
  globalThis.document = originalDocument;
}
process.stdout.write("background job polling contracts passed\n");
