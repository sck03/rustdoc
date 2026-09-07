import { createV3MoveConstraint, getElementBounds, reportDesignerV3ElementBounds } from "./reportDesignerGeometry.ts";
import { resolveReportDesignerLayerBands } from "./reportDesignerLayerBands.ts";
import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { clampReportDesignerV3ElementToPage, REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, type ReportDesignerV3Element, type ReportDesignerV3Layer, type ReportDesignerV3LayerRole, type ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

export const v3RegionNames: Record<ReportDesignerV3LayerRole, string> = { Header: "页眉", Body: "主体", Footer: "页脚", Overlay: "覆盖层" };

export function requiresV3BodyRegion(element: ReportDesignerV3Element) {
  return element.type === "Flow" && (element.flowKind === "DetailTable" || element.flowKind === "PageBreak");
}

export function resolveV3InsertionLayer(schema: ReportDesignerV3Schema, layerId: string, element: ReportDesignerV3Element) {
  const requested = schema.layers.find((layer) => layer.id === layerId);
  if (!requested || requested.locked || !requested.visible) return null;
  return requiresV3BodyRegion(element) && requested.role !== "Body"
    ? schema.layers.find((layer) => layer.role === "Body" && layer.visible && !layer.locked) ?? null
    : requested;
}

function regionBounds(schema: ReportDesignerV3Schema, role: ReportDesignerV3LayerRole) {
  const bands = resolveReportDesignerLayerBands(schema);
  return {
    top: role === "Body" ? bands.headerHeight : role === "Footer" ? bands.pageHeight - bands.footerHeight : 0,
    bottom: role === "Header" ? bands.headerHeight : role === "Body" ? bands.pageHeight - bands.footerHeight : bands.pageHeight,
  };
}

function regionTranslation(top: number, bottom: number, region: { top: number; bottom: number }) {
  return Math.round(bottom - top > region.bottom - region.top
    ? region.top - top
    : Math.min(region.bottom - bottom, Math.max(region.top - top, 0)));
}

export function placeV3ElementInRegion(schema: ReportDesignerV3Schema, layer: ReportDesignerV3Layer, element: ReportDesignerV3Element) {
  const clamped = clampReportDesignerV3ElementToPage(element, schema.page);
  if (layer.role === "Overlay") return clamped;
  const bounds = reportDesignerV3ElementBounds(clamped);
  const dy = regionTranslation(bounds.top, bounds.bottom, regionBounds(schema, layer.role));
  return dy ? clampReportDesignerV3ElementToPage({ ...clamped, yHundredthMm: clamped.yHundredthMm + dy }, schema.page) : clamped;
}

/** Cache once at pointer-down; preview and commit use the same movement bounds. */
export function createV3RegionMoveConstraint(schema: ReportDesignerV3Schema, elements: Iterable<ReportDesignerV3Element>) {
  const movable = Array.from(elements);
  const constraint = createV3MoveConstraint(schema, movable);
  if (!constraint) return null;
  const bodyElements = movable.filter(requiresV3BodyRegion);
  if (!bodyElements.length) return constraint;
  const bounds = getElementBounds(bodyElements.map((element) => ({ element })));
  const region = regionBounds(schema, "Body");
  if (bounds.bottom - bounds.top > region.bottom - region.top) return constraint;
  return { ...constraint, minimumDeltaY: Math.max(constraint.minimumDeltaY, region.top - bounds.top), maximumDeltaY: Math.min(constraint.maximumDeltaY, region.bottom - bounds.bottom) };
}

/** Coordinates are page-relative. Reparenting never adds a second origin offset. */
export function reassignV3MovedElements(state: ReportDesignerV3DocumentState, ids: Set<string>): ReportDesignerV3DocumentState | null {
  const bands = resolveReportDesignerLayerBands(state.schema);
  const targets = new Map<string, string>();
  for (const layer of state.schema.layers) {
    for (const element of layer.elements) {
      if (!ids.has(element.id) || element.locked || layer.locked) continue;
      const centerY = element.yHundredthMm + element.heightHundredthMm / 2;
      const role = requiresV3BodyRegion(element) ? "Body" : layer.role === "Overlay" ? "Overlay"
        : centerY < bands.headerHeight ? "Header" : centerY >= bands.pageHeight - bands.footerHeight ? "Footer" : "Body";
      if (role === layer.role) continue;
      const target = state.schema.layers.find((candidate) => candidate.role === role && candidate.visible && !candidate.locked);
      if (!target) return null;
      targets.set(element.id, target.id);
    }
  }
  return transferV3Elements(state, targets);
}

export function moveV3SelectionToRegion(state: ReportDesignerV3DocumentState, layerId: string): ReportDesignerV3DocumentState {
  const target = state.schema.layers.find((layer) => layer.id === layerId && layer.visible && !layer.locked);
  if (!target) return state;
  const ids = new Set(state.selectedIds);
  const elements = state.schema.layers.flatMap((layer) => layer.locked ? [] : layer.elements.filter((element) => ids.has(element.id) && !element.locked));
  if (!elements.length || (target.role !== "Body" && elements.some(requiresV3BodyRegion))) return state;
  const bounds = getElementBounds(elements.map((element) => ({ element })));
  const dy = target.role === "Overlay" ? 0 : regionTranslation(bounds.top, bounds.bottom, regionBounds(state.schema, target.role));
  const targets = new Map(elements.map((element) => [element.id, target.id]));
  const moved = dy ? { ...state, schema: { ...state.schema, layers: state.schema.layers.map((layer) => ({ ...layer,
    elements: layer.elements.map((element) => targets.has(element.id) ? clampReportDesignerV3ElementToPage({ ...element, yHundredthMm: element.yHundredthMm + dy }, state.schema.page) : element),
  })) } } : state;
  return transferV3Elements(moved, targets) ?? state;
}

function transferV3Elements(state: ReportDesignerV3DocumentState, targets: Map<string, string>): ReportDesignerV3DocumentState | null {
  const incoming = new Map<string, ReportDesignerV3Element[]>();
  const outgoing = new Set<string>();
  for (const layer of state.schema.layers) {
    for (const element of layer.elements) {
      const targetId = targets.get(element.id);
      if (!targetId || targetId === layer.id) continue;
      outgoing.add(element.id);
      const additions = incoming.get(targetId) ?? [];
      additions.push(element);
      incoming.set(targetId, additions);
    }
  }
  if (!outgoing.size) return state;
  const layers = state.schema.layers.map((layer) => {
    const kept = layer.elements.filter((element) => !outgoing.has(element.id));
    const additions = incoming.get(layer.id) ?? [];
    if (kept.length === layer.elements.length && !additions.length) return layer;
    const topZ = kept.reduce((top, element) => Math.max(top, element.zIndex), -1);
    return { ...layer, elements: [...kept, ...additions.map((element, index) => ({ ...element, zIndex: topZ + index + 1 }))] };
  });
  if (layers.some((layer) => layer.elements.length > REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER)) return null;
  return { ...state, schema: { ...state.schema, layers }, activeLayerId: targets.get(state.selectedIds[0]) ?? state.activeLayerId };
}
