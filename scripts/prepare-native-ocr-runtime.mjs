// Docker build component: extract only the governed native ONNX library.
import { createHash, randomUUID } from "node:crypto";
import { createReadStream, createWriteStream } from "node:fs";
import { lstat, mkdir, readFile, rename, rm, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Readable, Transform } from "node:stream";
import { pipeline } from "node:stream/promises";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const rid = process.argv[2];
if (!/^linux-(x64|arm64)$/.test(rid ?? "")) throw new Error("Specify linux-x64 or linux-arm64.");
const destination = path.resolve(process.argv[3] ?? path.join(root, "artifacts", "native-ocr-runtime", rid));
await managed(destination);
const lock = JSON.parse(await readFile(path.join(root, "src/ExportDocManager.Infrastructure.PdfOcr/packages.lock.json"), "utf8"));
const entries = Object.values(lock.dependencies).map((target) => target["Microsoft.ML.OnnxRuntime"]).filter(Boolean);
if (entries.length === 0 || new Set(entries.map((item) => `${item.resolved}:${item.contentHash}`)).size !== 1) throw new Error("ONNX Runtime must have one exact locked version and checksum.");
const { resolved: version, contentHash } = entries[0];
if (!/^\d+\.\d+\.\d+$/.test(version) || Buffer.from(contentHash, "base64").length !== 64) throw new Error("Invalid ONNX Runtime lock entry.");
const cache = path.join(root, ".codex-runtime", "native-runtime-packages");
await managed(cache);
await mkdir(cache, { recursive: true });
const archive = path.join(cache, `microsoft.ml.onnxruntime.${version}.nupkg`);
if (!await matches(archive, contentHash)) {
  const temporary = `${archive}.${randomUUID()}.download`;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 600_000);
  try {
    const response = await fetch(`https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime/${version}/microsoft.ml.onnxruntime.${version}.nupkg`, { signal: controller.signal, redirect: "error" });
    if (!response.ok || !response.body) throw new Error(`NuGet download failed: HTTP ${response.status}`);
    let size = 0;
    const limit = new Transform({ transform(bytes, _encoding, callback) {
      size += bytes.length;
      callback(size > 2 * 1024 ** 3 ? new Error("ONNX package exceeds 2 GiB.") : null, bytes);
    } });
    await pipeline(Readable.fromWeb(response.body), limit, createWriteStream(temporary, { flags: "wx" }), { signal: controller.signal });
    if (!await matches(temporary, contentHash)) throw new Error("ONNX Runtime package SHA-512 does not match packages.lock.json.");
    await rename(temporary, archive);
  } finally { clearTimeout(timer); await rm(temporary, { force: true }); }
}
await mkdir(destination, { recursive: true });
const child = spawnProcessTree("unzip", ["-j", "-o", archive, `runtimes/${rid}/native/libonnxruntime.so`, "-d", destination], { cwd: root, stdio: "inherit", windowsHide: true });
let extractionTimer;
try {
  await new Promise((resolve, reject) => {
    extractionTimer = setTimeout(() => reject(new Error("Native runtime extraction timed out.")), 60_000);
    child.once("error", reject);
    child.once("exit", (code) => code === 0 ? resolve() : reject(new Error(`unzip exited with ${code}`)));
  });
} finally { clearTimeout(extractionTimer); await stopProcessTree(child); }
const library = path.join(destination, "libonnxruntime.so");
await managed(library);
const info = await stat(library);
if (!info.isFile() || info.size <= 0 || info.size > 256 * 1024 ** 2) throw new Error("Extracted ONNX Runtime library is invalid.");
process.stdout.write(`Governed ONNX Runtime ${version} (${rid}) prepared.\n`);

async function matches(file, expected) {
  try {
    const hash = createHash("sha512");
    for await (const chunk of createReadStream(file)) hash.update(chunk);
    return hash.digest("base64") === expected;
  } catch (error) { if (error.code === "ENOENT") return false; throw error; }
}
async function managed(candidate) {
  const relative = path.relative(root, candidate);
  if (!relative || relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) throw new Error("Native runtime assets must stay in the workspace.");
  for (let current = candidate; current !== root; current = path.dirname(current)) {
    try { if ((await lstat(current)).isSymbolicLink()) throw new Error(`Linked runtime path: ${current}`); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
  }
}
