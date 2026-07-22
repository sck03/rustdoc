import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "service-availability-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "ui", "serviceAvailabilityModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { buildServiceReadinessUrl, getServiceConnectionLabel, resolveServiceConnectionState } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
assert(buildServiceReadinessUrl("http://127.0.0.1:5188") === "http://127.0.0.1:5188/readyz", "readiness URL should use API origin");
assert(buildServiceReadinessUrl("https://server.example/base/") === "https://server.example/base/readyz", "readiness URL should preserve configured base path");
assert(resolveServiceConnectionState({ isDesktopRuntime: false, isOnline: false, availability: "available" }) === "device-offline", "browser offline state should take precedence");
assert(resolveServiceConnectionState({ isDesktopRuntime: true, isOnline: false, availability: "available" }) === "available", "desktop localhost service should ignore external offline state");
assert(resolveServiceConnectionState({ isDesktopRuntime: false, isOnline: true, availability: "unreachable" }) === "unreachable", "online device should expose unreachable API");
assert(getServiceConnectionLabel("checking") === "正在检查服务" && getServiceConnectionLabel("unreachable") === "服务暂不可用", "service labels should explain state");
process.stdout.write("service availability model tests passed\n");
