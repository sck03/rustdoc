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
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modulePath)}; import * as scene from ${JSON.stringify(sceneModulePath)}; import * as projection from ${JSON.stringify(path.join(path.dirname(modulePath), 'ContainerPackingProjectionModel.tsx'))}; import * as pseudo from ${JSON.stringify(path.join(path.dirname(modulePath), 'containerPackingPseudo3dModel.ts'))}; import * as request from ${JSON.stringify(path.join(path.dirname(modulePath), 'containerPackingRequest.ts'))}; import * as draft from ${JSON.stringify(path.join(path.dirname(modulePath), 'containerPackingProjectDraft.ts'))}; Object.assign(globalThis, {__model:model,__scene:scene,__projection:projection,__pseudo:pseudo,__request:request,__draft:draft});`, "utf8");
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
const draft = globalThis.__draft.createPackingProjectDraft();
draft.container = {containerType:"测试",length:"100",width:"100",height:"100",volume:"1",maxWeight:"100"};
draft.rules.defaultPalletHeight = "0";
draft.cargoRows[0].height = "12.3456"; draft.cargoRows[0].weight = "0.0004";
const build = () => globalThis.__request.buildContainerPackingAnalyzeRequest(draft.container,draft.cargoRows,draft.rules);
const saved = build(), restored = globalThis.__draft.packingDraftFromProject({...saved,name:"测试",containerType:"测试"});
assert(restored.rules.defaultPalletHeight === "0", "explicit zero pallet height survives saved draft hydration");
assert(restored.cargoRows[0].height === "12.3456" && restored.cargoRows[0].weight === "0.0004", "saved decimals retain full precision");
for(const value of ["1.0000000000000001","0.00000000000000000000000000001"]) {
  draft.cargoRows[0].height=value;
  assert(globalThis.__request.validatePackingInput(build).error?.includes("精度"), "unrepresentable precision is rejected before submission");
}
const overview = globalThis.__pseudo.buildPseudo3dProjection({width:960,height:500}, dimensions);
assert(overview.headLabel.x < overview.doorLabel.x, 'overview has the head on the left and door on the right');
const {buildContainerPackingProjection, projectPackedCargoItem, renderContainerPackingItemGrid} = globalThis.__projection;
for (const view of ['top', 'side']) {
  const plane = buildContainerPackingProjection(view, dimensions);
  assert(projectPackedCargoItem(createItem(), view, dimensions, plane).x < projectPackedCargoItem(createItem({x:80}), view, dimensions, plane).x, `${view} preserves the same head-to-door direction`);
}
assert(renderContainerPackingItemGrid(createItem({loadCount:6}), 'top', {x:0,y:0,width:100,height:100}) === null, 'stacked cartons do not invent extra floor positions in the top view');
assert(globalThis.__scene.buildContainerPackingSceneData({packedItems:[createItem({isPalletized:true,unitsRepresented:100,loadCount:1})]},dimensions).boxes[0].loadCount === 1, 'cartons on a pallet are not drawn as stacked pallets');

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
  if (object.isMesh && object.material.vertexColors) cargo.push(object);
  if (object.isLineSegments && object.material.opacity === 0.62) gridLines.push(object);
  if (object.geometry) resources.add(object.geometry);
  for (const material of Array.isArray(object.material) ? object.material : object.material ? [object.material] : []) resources.add(material);
});
assert(cargo.length === 1 && cargo[0].geometry.attributes.position.count === 2 * 36, 'all cargo faces share one draw object without losing vertices');
assert(cargo[0].geometry.groups.length === 0, 'face shading uses vertex colors without separate draw groups');
assert(gridLines.length === 1 && gridLines[0].geometry.attributes.position.count === 2 * 17 * 8, 'all layer segments share one draw object without losing geometry');
const disposed = new Map();
for (const resource of resources) resource.addEventListener('dispose', () => disposed.set(resource, (disposed.get(resource) ?? 0) + 1));
disposeContainerPackingScene(root);
assert(disposed.size === resources.size && [...disposed.values()].every(count => count === 1), 'shared GPU resources are disposed exactly once');

const measurements = [];
for (const count of [50,500,5000]) {
  const sample = {packedItems:Array.from({length:count},(_,i)=>createItem({x:i*11,name:'货物'+i}))};
  const data=buildContainerPackingSceneData(sample,{length:60000,width:100,height:100});
  const start=performance.now(), root=buildContainerPackingRoot(data,sample,false);
  const geometries=new Set(), materials=new Set(); let groups=0;
  root.traverse(object=>{ if(object.geometry){geometries.add(object.geometry); groups+=Array.isArray(object.material)?object.geometry.groups.length:1;}
    for(const material of Array.isArray(object.material)?object.material:object.material?[object.material]:[])materials.add(material); });
  measurements.push({boxes:count,buildMs:performance.now()-start,geometries:geometries.size,materials:materials.size,drawGroups:groups});
  assert(geometries.size<=10 && materials.size<=10 && groups<=16,'5000 boxes retain a bounded number of GPU resources and draw groups');
  disposeContainerPackingScene(root);
}
fs.writeFileSync(path.join(workspace,'measurements.json'),JSON.stringify(measurements,null,2));
console.log(JSON.stringify(measurements));
process.stdout.write("container packing render-block tests passed\n");
