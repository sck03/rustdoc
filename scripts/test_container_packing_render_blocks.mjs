import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "container-packing-render-block-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
const modulePath = path.join(
  repoRoot,
  "apps",
  "export-doc-web",
  "src",
  "features",
  "tools",
  "container-packing",
  "containerPackingRenderBlocks.ts",
).replaceAll("\\", "/");

fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modulePath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { mergePackedItemsForContainerRender } = globalThis.__model;
const dimensions = { length: 100, width: 100, height: 100 };
const createItem = (overrides = {}) => ({
  name: "纸箱",
  colorArgb: -16744448,
  isRotated: false,
  isPalletized: false,
  x: 0,
  y: 0,
  width: 10,
  height: 10,
  baseHeight: 0,
  occupiedHeight: 10,
  topHeight: 10,
  unitsRepresented: 1,
  loadCount: 1,
  totalWeight: 5,
  priorityGroup: "A",
  preferredZone: "Door",
  ...overrides,
});
const assert = (condition, message) => { if (!condition) throw new Error(message); };

const merged = mergePackedItemsForContainerRender([
  createItem(),
  createItem({ y: 10 }),
], dimensions);
assert(merged.length === 1, "contiguous compatible cargo should share one render block");
assert(merged[0].widthSegments === 2 && merged[0].loadCount === 2, "merged block should retain grid and load totals");

const priorityGroups = mergePackedItemsForContainerRender([
  createItem(),
  createItem({ y: 10, priorityGroup: "B" }),
], dimensions);
assert(priorityGroups.length === 2, "different priority groups must not be merged");

const preferredZones = mergePackedItemsForContainerRender([
  createItem(),
  createItem({ y: 10, preferredZone: "Head" }),
], dimensions);
assert(preferredZones.length === 2, "different preferred zones must not be merged");

const palletized = mergePackedItemsForContainerRender([
  createItem({ isPalletized: true, loadCount: 12, unitsRepresented: 12 }),
], dimensions);
assert(palletized.length === 1 && palletized[0].heightSegments === 1, "palletized cargo should remain a single block");

process.stdout.write("container packing render-block tests passed\n");
