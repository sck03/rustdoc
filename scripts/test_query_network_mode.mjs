import { createRequire } from "node:module";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "query-network-mode-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");

fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "api", "queryNetworkMode.ts")
  .replaceAll("\\", "/");
fs.writeFileSync(
  entry,
  `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`,
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({
  entryPoints: [entry],
  outfile: bundle,
  bundle: true,
  format: "esm",
  platform: "node",
  logLevel: "silent",
});
await import(pathToFileURL(bundle).href);

const { resolveQueryNetworkMode } = globalThis.__model;
assert.equal(resolveQueryNetworkMode(true), "always", "Desktop queries must continue to reach localhost while offline.");
assert.equal(resolveQueryNetworkMode(false), "online", "Browser deployments should retain online-aware query pausing.");

process.stdout.write("query network mode tests passed\n");
