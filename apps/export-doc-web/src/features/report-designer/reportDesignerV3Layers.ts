import { createV3ElementId } from "./reportDesignerV3ElementFactories.ts";
import { reportDesignerLayerHeight } from "./reportDesignerLayerBands.ts";
import { v3RegionNames } from "./reportDesignerV3Regions.ts";
import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { REPORT_DESIGNER_V3_MAX_LAYER_COUNT, type ReportDesignerV3Layer, type ReportDesignerV3LayerRole } from "./reportDesignerV3Schema.ts";

export function addV3Layer(state: ReportDesignerV3DocumentState, role: ReportDesignerV3LayerRole): ReportDesignerV3DocumentState {
  const { layers } = state.schema;
  if (layers.length >= REPORT_DESIGNER_V3_MAX_LAYER_COUNT || !Object.hasOwn(v3RegionNames, role)) return state;
  const ids = new Set(layers.flatMap(layer => [layer.id, ...layer.elements.map(element => element.id)]));
  let id = createV3ElementId("layer");
  while (ids.has(id)) id = createV3ElementId("layer");
  let number = 1;
  let name = v3RegionNames[role];
  while (layers.some(layer => layer.name === name)) name = `${v3RegionNames[role]} ${++number}`;
  const sameRole = layers.find(layer => layer.role === role && layer.visible);
  const layer: ReportDesignerV3Layer = {
    id, name, role, visible: true, locked: false, elements: [],
    print: { repeatOnEveryPage: role === "Header" || role === "Footer", keepTogether: role !== "Body", pinToPageBottom: false, minHeightHundredthMm: 0 },
    ...(sameRole ? { designHeightHundredthMm: reportDesignerLayerHeight(sameRole) } : {}),
  };
  return { ...state, schema: { ...state.schema, layers: [...layers, layer] }, activeLayerId: id, selectedIds: [] };
}

export function getV3LayerRemovalIssue(state: ReportDesignerV3DocumentState, layerId: string) {
  const layer = state.schema.layers.find(candidate => candidate.id === layerId);
  if (!layer) return "图层不存在。";
  if (layer.locked) return "请先解锁图层。";
  if (layer.elements.length) return "请先移走或删除图层中的内容。";
  if ((layer.role === "Body" || layer.role === "Overlay") && !state.schema.layers.some(candidate => candidate.id !== layerId && candidate.role === layer.role)) {
    return `至少保留一个${v3RegionNames[layer.role]}图层。`;
  }
  return null;
}

export function removeV3Layer(state: ReportDesignerV3DocumentState, layerId: string): ReportDesignerV3DocumentState {
  if (getV3LayerRemovalIssue(state, layerId)) return state;
  const layers = state.schema.layers.filter(layer => layer.id !== layerId);
  return { ...state, schema: { ...state.schema, layers },
    activeLayerId: state.activeLayerId === layerId ? (layers.find(layer => layer.visible)?.id ?? layers[0].id) : state.activeLayerId };
}

export function moveV3Layer(state: ReportDesignerV3DocumentState, layerId: string, direction: -1 | 1): ReportDesignerV3DocumentState {
  const index = state.schema.layers.findIndex(layer => layer.id === layerId);
  const target = index + direction;
  const layers = state.schema.layers;
  if (index < 0 || !layers[target] || layers[index].locked || layers[target].locked) return state;
  const reordered = [...layers];
  [reordered[index], reordered[target]] = [reordered[target], reordered[index]];
  return { ...state, schema: { ...state.schema, layers: reordered } };
}

export function updateV3Layer(
  state: ReportDesignerV3DocumentState,
  layerId: string,
  update: Partial<Pick<ReportDesignerV3Layer, "name" | "visible" | "locked" | "designHeightHundredthMm" | "print">>,
): ReportDesignerV3DocumentState {
  const current = state.schema.layers.find(layer => layer.id === layerId);
  if (!current) return state;
  const next = {
    ...current,
    ...update,
    name: typeof update.name === "string" ? update.name.trim().slice(0, 120) || current.name : current.name,
    visible: typeof update.visible === "boolean" ? update.visible : current.visible,
    locked: typeof update.locked === "boolean" ? update.locked : current.locked,
    print: normalizeLayerPrint(update.print ?? current.print, current.role),
  };
  if (next.name === current.name && next.visible === current.visible && next.locked === current.locked && next.designHeightHundredthMm === current.designHeightHundredthMm &&
      sameLayerPrint(next.print, current.print)) return state;
  return { ...state, schema: { ...state.schema, layers: state.schema.layers.map(layer => layer.id === layerId ? next : layer) } };
}

function sameLayerPrint(left: ReportDesignerV3Layer["print"], right: ReportDesignerV3Layer["print"]) {
  return left.repeatOnEveryPage === right.repeatOnEveryPage &&
    left.keepTogether === right.keepTogether &&
    left.pinToPageBottom === right.pinToPageBottom &&
    (left.followBody === true) === (right.followBody === true) &&
    (left.firstPageOnly === true) === (right.firstPageOnly === true) &&
    left.minHeightHundredthMm === right.minHeightHundredthMm;
}

function normalizeLayerPrint(value: ReportDesignerV3Layer["print"] | undefined, role: ReportDesignerV3LayerRole): ReportDesignerV3Layer["print"] {
  const source = (value && typeof value === "object" ? value : {}) as Partial<ReportDesignerV3Layer["print"]>;
  const requestedHeight = Number(source.minHeightHundredthMm);
  return {
    repeatOnEveryPage: role === "Body" ? false : source.repeatOnEveryPage === true,
    keepTogether: source.keepTogether === true,
    pinToPageBottom: role === "Footer" && source.pinToPageBottom === true,
    followBody: role === "Footer" && source.pinToPageBottom !== true && source.followBody === true,
    firstPageOnly: source.firstPageOnly === true,
    minHeightHundredthMm: Number.isFinite(requestedHeight) ? Math.min(26000, Math.max(0, Math.round(requestedHeight))) : 0,
  };
}
