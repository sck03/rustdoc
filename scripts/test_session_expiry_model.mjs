import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "session-expiry-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "api", "sessionExpiryModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { calculateSessionExpiryDelay, maximumBrowserTimeoutMs } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const now = Date.parse("2026-07-22T06:00:00.000Z");
assert(calculateSessionExpiryDelay("invalid", now) === null, "invalid expiry should not schedule a timer");
assert(calculateSessionExpiryDelay("", now) === null, "missing expiry should not keep a stored session alive");
assert(calculateSessionExpiryDelay("2026-07-22T05:59:59.000Z", now) === 0, "expired session should end immediately");
assert(calculateSessionExpiryDelay("2026-07-22T06:00:30.000Z", now) === 30_000, "future expiry should use exact delay");
assert(calculateSessionExpiryDelay("2099-01-01T00:00:00.000Z", now) === maximumBrowserTimeoutMs, "long expiry should respect browser timeout limit");
process.stdout.write("session expiry model tests passed\n");
