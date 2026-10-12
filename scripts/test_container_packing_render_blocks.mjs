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
const sceneModulePath = path.join(path.dirname(modulePath), 'containerPackingSceneObjects.ts').replaceAll('\\', '/');
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modulePath)}; import * as scene from ${JSON.stringify(sceneModulePath)}; globalThis.__model = model; globalThis.__scene = scene;`, "utf8");
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

const { buildContainerPackingRoot, buildContainerPackingSceneData, disposeContainerPackingScene } = globalThis.__scene;
const analysis = { packedItems: [createItem({ loadCount: 18, unitsRepresented: 18 }), createItem({ x: 30, loadCount: 18, unitsRepresented: 18 })] };
const sceneData = buildContainerPackingSceneData(analysis, dimensions);
const root = buildContainerPackingRoot(sceneData, analysis, true);
const cargo = [], gridLines = [], resources = new Set();
root.traverse(object => {
  if (object.isMesh && object.name === '纸箱') cargo.push(object);
  if (object.isLineSegments && object.material.opacity === 0.62) gridLines.push(object);
  if (object.geometry) resources.add(object.geometry);
  for (const material of Array.isArray(object.material) ? object.material : object.material ? [object.material] : []) resources.add(material);
});
assert(cargo.length === 2 && cargo[0].material === cargo[1].material, 'cargo of the same color shares its materials');
assert(new Set(cargo[0].material).size === 5, 'identical side faces reuse a material');
assert(gridLines.length === 2 && gridLines.every(lines => lines.geometry.attributes.position.count === 17 * 8), 'all layer segments use one draw object per cargo block without losing geometry');
const disposed = new Map();
for (const resource of resources) resource.addEventListener('dispose', () => disposed.set(resource, (disposed.get(resource) ?? 0) + 1));
disposeContainerPackingScene(root);
assert(disposed.size === resources.size && [...disposed.values()].every(count => count === 1), 'shared GPU resources are disposed exactly once');

process.stdout.write("container packing render-block tests passed\n");
