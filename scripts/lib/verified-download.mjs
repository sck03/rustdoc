import { createHash, randomUUID } from "node:crypto";
import { createReadStream, createWriteStream } from "node:fs";
import { rename, rm } from "node:fs/promises";
import { Readable, Transform } from "node:stream";
import { pipeline } from "node:stream/promises";
import { setTimeout as delay } from "node:timers/promises";
import path from "node:path";

const publications = new Map();
async function publishFile(source, destination, signal) {
  const absolute = path.resolve(destination);
  const key = process.platform === "win32" ? absolute.toLowerCase() : absolute;
  const previous = publications.get(key) ?? Promise.resolve();
  const publication = previous.catch(() => {}).then(async () => {
    for (let attempt = 0; ; attempt++) {
      signal.throwIfAborted();
      try { await rename(source, destination); return; }
      catch (error) {
        if (process.platform !== "win32" || !["EPERM", "EBUSY", "EACCES"].includes(error.code) || attempt >= 4) throw error;
        await delay(50 * 2 ** attempt, undefined, { signal });
      }
    }
  });
  publications.set(key, publication);
  try { await publication; }
  finally { if (publications.get(key) === publication) publications.delete(key); }
}

export async function fileSha256(filePath, { signal } = {}) {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(filePath, { signal })) hash.update(chunk);
  return hash.digest("hex").toUpperCase();
}

export async function downloadVerifiedFile(url, destinationPath, { sha256, maxBytes, timeoutMs = 120_000, signal } = {}) {
  if (!/^[A-F0-9]{64}$/iu.test(sha256 || "") || !Number.isSafeInteger(maxBytes) || maxBytes <= 0
      || !Number.isSafeInteger(timeoutMs) || timeoutMs <= 0) {
    throw new Error("A verified download requires SHA-256, a byte limit and a positive timeout.");
  }
  const downloadSignal = AbortSignal.any([AbortSignal.timeout(timeoutMs), ...(signal ? [signal] : [])]);
  const temporaryPath = `${destinationPath}.${randomUUID()}.download`;
  let response;
  try {
    response = await fetch(url, { redirect: "follow", signal: downloadSignal });
    if (!response.ok || !response.body) throw new Error(`Download failed: HTTP ${response.status} ${response.statusText}`);
    const length = response.headers.get("content-length");
    if (length !== null && (!/^\d+$/u.test(length) || Number(length) > maxBytes)) throw new Error(`Download exceeds ${maxBytes} bytes.`);
    const hash = createHash("sha256");
    let received = 0;
    const verifier = new Transform({
      transform(chunk, encoding, callback) {
        received += chunk.length;
        if (received > maxBytes) return callback(new Error(`Download exceeds ${maxBytes} bytes.`));
        hash.update(chunk);
        callback(null, chunk);
      },
    });
    await pipeline(Readable.fromWeb(response.body), verifier, createWriteStream(temporaryPath, { flags: "wx" }), { signal: downloadSignal });
    const actualHash = hash.digest("hex").toUpperCase();
    if (actualHash !== sha256.toUpperCase()) throw new Error(`SHA-256 mismatch: expected ${sha256}, received ${actualHash}.`);
    downloadSignal.throwIfAborted();
    await publishFile(temporaryPath, destinationPath, downloadSignal);
  } finally {
    if (response?.body && !response.body.locked) await response.body.cancel().catch(() => {});
    await rm(temporaryPath, { force: true });
  }
}
