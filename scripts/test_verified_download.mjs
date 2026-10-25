import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { createServer } from "node:http";
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { downloadVerifiedFile } from "./lib/verified-download.mjs";

const runtimeRoot = path.resolve(import.meta.dirname, "../.codex-runtime");
await mkdir(runtimeRoot, { recursive: true });
const fixture = await mkdtemp(path.join(runtimeRoot, "verified-download-"));
const content = Buffer.from("verified font fixture");
const sha256 = createHash("sha256").update(content).digest("hex");
const server = createServer((request, response) => {
  if (request.url === "/stall") return;
  if (request.url === "/large") {
    response.writeHead(200, { "content-length": 1_000 });
    response.end(content);
  } else if (request.url === "/chunked-large") {
    response.writeHead(200);
    response.write(content);
    response.end(content);
  } else {
    response.end(content);
  }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const destination = path.join(fixture, "font.otf");
const options = { sha256, maxBytes: content.length, timeoutMs: 5_000 };
try {
  await writeFile(destination, "previous verified bytes");
  for (const [route, overrides] of [["/large", {}], ["/chunked-large", {}], ["/valid", { sha256: "0".repeat(64) }], ["/stall", { timeoutMs: 50 }]]) {
    await assert.rejects(downloadVerifiedFile(base + route, destination, { ...options, ...overrides }));
    assert.equal(await readFile(destination, "utf8"), "previous verified bytes");
    assert.deepEqual(await readdir(fixture), ["font.otf"]);
  }
  const cancellation = new AbortController();
  const cancelledDownload = downloadVerifiedFile(base + "/stall", destination, { ...options, signal: cancellation.signal });
  cancellation.abort();
  await assert.rejects(cancelledDownload);
  await Promise.all([downloadVerifiedFile(base + "/valid", destination, options), downloadVerifiedFile(base + "/valid", destination, options)]);
  assert.deepEqual(await readFile(destination), content);
  assert.deepEqual(await readdir(fixture), ["font.otf"]);
  console.log("Verified downloads preserve outputs across oversized, corrupt, timed out, cancelled and concurrent requests.");
} finally {
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
  await rm(fixture, { recursive: true, force: true });
}
