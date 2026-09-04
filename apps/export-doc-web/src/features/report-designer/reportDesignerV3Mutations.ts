import type { ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";
import {
  clampReportDesignerV3ElementToPage, mmToHundredthMm,
  REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS,
  reportDesignerV3PageDimensions, type ReportDesignerV3Element,
  type ReportDesignerV3Layer, type ReportDesignerV3Page, type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import {
  createV3MoveConstraint,
  getElementBounds,
  reportDesignerV3ElementBounds,
  resolveV3MoveDeltaFromConstraint,
} from "./reportDesignerGeometry.ts";
import {
  createV3ElementId, createV3FieldElement, createV3FlowElement, createV3ImageElement,
  createV3LineElement, createV3PageNumberElement, createV3RectangleElement, createV3TextElement,
} from "./reportDesignerV3ElementFactories.ts";
import { resizeV3Element as resizeV3ElementLocal } from "./reportDesignerV3Resize.ts";
export {
  createV3ElementId, createV3FieldElement, createV3FlowElement, createV3ImageElement,
  createV3LineElement, createV3PageNumberElement, createV3RectangleElement, createV3TextElement,
} from "./reportDesignerV3ElementFactories.ts";
export { resizeV3ElementLocal as resizeV3Element };

export type ReportDesignerV3DocumentState = {
  schema: ReportDesignerV3Schema;
  selectedIds: string[];
  activeLayerId: string | null;
};

export type ReportDesignerV3ResizeDirection = "n" | "ne" | "e" | "se" | "s" | "sw" | "w" | "nw";
export type ReportDesignerV3Alignment = "left" | "center-horizontal" | "right" | "top" | "center-vertical" | "bottom";
export type ReportDesignerV3Distribution = "horizontal" | "vertical";

export function createReportDesignerV3DocumentState(schema: ReportDesignerV3Schema): ReportDesignerV3DocumentState {
  const firstLayer = schema.layers.find((layer) => layer.visible) ?? schema.layers[0];
  const firstElement = firstLayer?.elements.find((element) => element.visible);
  return {
    schema,
    selectedIds: firstElement ? [firstElement.id] : [],
    activeLayerId: firstLayer?.id ?? null,
  };
}

export function updateV3Element(
  state: ReportDesignerV3DocumentState,
  elementId: string,
  update: Partial<ReportDesignerV3Element>,
): ReportDesignerV3DocumentState {
  const located = findV3Element(state.schema, elementId);
  if (!located || located.layer.locked) return state;

  // A locked element can still be hidden, excluded from output, or explicitly
  // unlocked.  Geometry/content/style edits remain blocked until it is unlocked.
  if (located.element.locked) {
    const allowedKeys = new Set(["visible", "outputEnabled", "locked"]);
    if (Object.keys(update).some((key) => !allowedKeys.has(key))) return state;
    if (update.locked !== undefined && update.locked !== false) return state;
  }

  const nextElement = clampReportDesignerV3ElementToPage(
    {
      ...located.element,
      ...update,
      id: located.element.id,
      type: located.element.type,
    } as ReportDesignerV3Element,
    state.schema.page,
  );
  if (areV3ElementsEqual(located.element, nextElement)) return state;
  return updateV3Elements(state, new Set([elementId]), (element) =>
    element.id === elementId ? nextElement : element);
}

export function insertV3Element(
  state: ReportDesignerV3DocumentState,
  layerId: string,
  element: ReportDesignerV3Element,
): ReportDesignerV3DocumentState {
  const layer = state.schema.layers.find((candidate) => candidate.id === layerId);
  if (!layer || layer.locked) return state;
  if (layer.elements.length >= REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER ||
      countV3Elements(state.schema) >= REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) {
    return state;
  }
  const existingIds = new Set([
    ...state.schema.layers.map((candidate) => candidate.id),
    ...state.schema.layers.flatMap((candidate) => candidate.elements.map((item) => item.id)),
  ]);
  let elementId = typeof element.id === "string" ? element.id.trim() : "";
  if (!elementId || existingIds.has(elementId)) {
    elementId = createV3ElementId(element.type.toLowerCase());
    while (existingIds.has(elementId)) elementId = createV3ElementId(element.type.toLowerCase());
  }
  const topZIndex = layer.elements.reduce((max, item) => Math.max(max, item.zIndex), -1) + 1;
  const nextElement = clampReportDesignerV3ElementToPage(
    { ...element, id: elementId, zIndex: topZIndex },
    state.schema.page,
  );
  return {
    ...state,
    schema: {
      ...state.schema,
      layers: state.schema.layers.map((candidate) => candidate.id === layerId
        ? { ...candidate, elements: [...candidate.elements, nextElement] }
        : candidate),
    },
    selectedIds: [elementId],
    activeLayerId: layerId,
  };
}

export function updateV3Page(
  state: ReportDesignerV3DocumentState,
  update: Partial<ReportDesignerV3Page>,
): ReportDesignerV3DocumentState {
  // Runtime callers can still pass data from an uncontrolled form or an old
  // WebView even though TypeScript narrows the type.  Never let an invalid
  // orientation leak into the persisted schema; keep the current valid value
  // and derive dimensions from that canonical enum.
  const nextOrientation = update.orientation === "Landscape" || update.orientation === "Portrait"
    ? update.orientation
    : state.schema.page.orientation === "Landscape"
      ? "Landscape"
      : "Portrait";
  const dimensions = reportDesignerV3PageDimensions(nextOrientation);
  const nextPage: ReportDesignerV3Page = {
    ...state.schema.page,
    ...update,
    size: "A4",
    orientation: nextOrientation,
    widthHundredthMm: dimensions.width,
    heightHundredthMm: dimensions.height,
  };
  nextPage.marginTopHundredthMm = normalizeMargin(nextPage.marginTopHundredthMm, dimensions.height);
  nextPage.marginBottomHundredthMm = normalizeMargin(
    nextPage.marginBottomHundredthMm,
    Math.max(0, dimensions.height - nextPage.marginTopHundredthMm),
  );
  nextPage.marginLeftHundredthMm = normalizeMargin(nextPage.marginLeftHundredthMm, dimensions.width);
  nextPage.marginRightHundredthMm = normalizeMargin(
    nextPage.marginRightHundredthMm,
    Math.max(0, dimensions.width - nextPage.marginLeftHundredthMm),
  );
  const nextSchema: ReportDesignerV3Schema = {
    ...state.schema,
    page: nextPage,
    layers: state.schema.layers.map((layer) => ({
      ...layer,
      elements: layer.elements.map((element) => clampReportDesignerV3ElementToPage(element, nextPage)),
    })),
  };
  if (sameV3Page(state.schema.page, nextPage) &&
      nextSchema.layers.every((layer, index) => layer.elements.every((element, elementIndex) =>
        areV3ElementsEqual(element, state.schema.layers[index]?.elements[elementIndex] ?? element)))) {
    return state;
  }
  return {
    ...state,
    schema: nextSchema,
  };
}

export function updateV3Layer(
  state: ReportDesignerV3DocumentState,
  layerId: string,
  update: Partial<Pick<ReportDesignerV3Layer, "name" | "visible" | "locked" | "designHeightHundredthMm" | "print">>,
): ReportDesignerV3DocumentState {
  const current = state.schema.layers.find((layer) => layer.id === layerId);
  if (!current) return state;
  const nextPrint = normalizeLayerPrintForMutation(update.print ?? current.print, current.role);
  const next = {
    ...current,
    ...update,
    name: typeof update.name === "string" ? update.name : current.name,
    visible: typeof update.visible === "boolean" ? update.visible : current.visible,
    locked: typeof update.locked === "boolean" ? update.locked : current.locked,
    print: nextPrint,
  };
  if (next.name === current.name && next.visible === current.visible && next.locked === current.locked && next.designHeightHundredthMm === current.designHeightHundredthMm &&
      sameV3LayerPrint(next.print, current.print)) return state;
  return {
    ...state,
    schema: {
      ...state.schema,
      layers: state.schema.layers.map((layer) => layer.id === layerId
        ? { ...layer, ...next, print: nextPrint }
        : layer),
    },
  };
}

export function updateV3Grid(
  state: ReportDesignerV3DocumentState,
  update: Partial<ReportDesignerV3Schema["grid"]>,
): ReportDesignerV3DocumentState {
  const requestedSize = Number.isFinite(update.sizeHundredthMm)
    ? Math.round(update.sizeHundredthMm as number)
    : state.schema.grid.sizeHundredthMm;
  const nextGrid = {
    ...state.schema.grid,
    ...update,
    enabled: typeof update.enabled === "boolean" ? update.enabled : state.schema.grid.enabled,
    snap: typeof update.snap === "boolean" ? update.snap : state.schema.grid.snap,
    sizeHundredthMm: Math.min(
      5000,
      Math.max(100, requestedSize),
    ),
  };
  if (nextGrid.enabled === state.schema.grid.enabled &&
      nextGrid.snap === state.schema.grid.snap &&
      nextGrid.sizeHundredthMm === state.schema.grid.sizeHundredthMm) return state;
  return {
    ...state,
    schema: { ...state.schema, grid: nextGrid },
  };
}

export function moveSelectedV3Elements(
  state: ReportDesignerV3DocumentState,
  deltaX: number,
  deltaY: number,
  snap = state.schema.grid.snap,
): ReportDesignerV3DocumentState {
  const selected = new Set(state.selectedIds);
  if (selected.size === 0) return state;
  const { dx: boundedDx, dy: boundedDy, movable } = resolveV3MoveDelta(state, deltaX, deltaY, snap);
  if (movable.length === 0) return state;
  return updateV3Elements(state, selected, (element, layer) => {
    if (element.locked || layer.locked) return element;
    return clampReportDesignerV3ElementToPage({
      ...element,
      xHundredthMm: element.xHundredthMm + boundedDx,
      yHundredthMm: element.yHundredthMm + boundedDy,
    }, state.schema.page);
  });
}

/**
 * Resolve only the geometry delta for a move.  Canvas previews use this
 * helper without cloning the complete document on every animation frame;
 * the full mutation remains the single commit path on pointer-up.
 */
export function resolveV3MoveDelta(
  state: ReportDesignerV3DocumentState,
  deltaX: number,
  deltaY: number,
  snap = state.schema.grid.snap,
) {
  const selected = new Set(state.selectedIds);
  const movable = state.schema.layers.flatMap((layer) => layer.elements
    .filter((element) => selected.has(element.id) && !element.locked && !layer.locked));
  if (movable.length === 0) return { dx: 0, dy: 0, movable };
  const constraint = createV3MoveConstraint(state.schema, movable);
  const delta = resolveV3MoveDeltaFromConstraint(constraint, deltaX, deltaY, snap);
  return { ...delta, movable };
}

/**
 * Aligns every movable selected element to the selected set's bounding box.
 * Locked elements and locked layers are intentionally excluded from both the
 * reference bounds and the mutation, so a protected object can never be
 * changed indirectly by a bulk layout command.
 */
export function alignSelectedV3Elements(
  state: ReportDesignerV3DocumentState,
  alignment: ReportDesignerV3Alignment,
): ReportDesignerV3DocumentState {
  const movable = getMovableSelectedV3Elements(state);
  if (movable.length < 2) return state;
  const bounds = getElementBounds(movable);
  return updateV3Elements(state, new Set(movable.map(({ element }) => element.id)), (element, layer) => {
    if (element.locked || layer.locked) return element;
    const currentBounds = reportDesignerV3ElementBounds(element);
    const currentWidth = currentBounds.right - currentBounds.left;
    const currentHeight = currentBounds.bottom - currentBounds.top;
    const targetLeft = alignment === "left"
      ? bounds.left
      : alignment === "center-horizontal"
        ? (bounds.left + bounds.right - currentWidth) / 2
        : alignment === "right"
          ? bounds.right - currentWidth
          : currentBounds.left;
    const targetTop = alignment === "top"
      ? bounds.top
      : alignment === "center-vertical"
        ? (bounds.top + bounds.bottom - currentHeight) / 2
        : alignment === "bottom"
          ? bounds.bottom - currentHeight
          : currentBounds.top;
    return clampReportDesignerV3ElementToPage({
      ...element,
      xHundredthMm: element.xHundredthMm + targetLeft - currentBounds.left,
      yHundredthMm: element.yHundredthMm + targetTop - currentBounds.top,
    }, state.schema.page);
  });
}

/**
 * Distributes three or more selected objects with equal edge-to-edge gaps.
 * The outermost objects keep their positions; only the objects between them
 * move.  This is deterministic even when objects share the same coordinate.
 */
export function distributeSelectedV3Elements(
  state: ReportDesignerV3DocumentState,
  direction: ReportDesignerV3Distribution,
): ReportDesignerV3DocumentState {
  const movable = getMovableSelectedV3Elements(state);
  if (movable.length < 3) return state;
  const sorted = [...movable].sort((left, right) => {
    const leftBounds = reportDesignerV3ElementBounds(left.element);
    const rightBounds = reportDesignerV3ElementBounds(right.element);
    const primary = direction === "horizontal"
      ? leftBounds.left - rightBounds.left
      : leftBounds.top - rightBounds.top;
    return primary || left.element.id.localeCompare(right.element.id);
  });
  const bounds = getElementBounds(sorted);
  const start = direction === "horizontal" ? bounds.left : bounds.top;
  const end = direction === "horizontal" ? bounds.right : bounds.bottom;
  const occupied = sorted.reduce(
    (total, item) => {
      const itemBounds = reportDesignerV3ElementBounds(item.element);
      return total + (direction === "horizontal"
        ? itemBounds.right - itemBounds.left
        : itemBounds.bottom - itemBounds.top);
    },
    0,
  );
  const gap = Math.max(0, (end - start - occupied) / (sorted.length - 1));
  const positions = new Map<string, number>();
  let cursor = start;
  sorted.forEach((item) => {
    positions.set(item.element.id, Math.round(cursor));
    const itemBounds = reportDesignerV3ElementBounds(item.element);
    cursor += (direction === "horizontal"
      ? itemBounds.right - itemBounds.left
      : itemBounds.bottom - itemBounds.top) + gap;
  });
  return updateV3Elements(state, new Set(sorted.map(({ element }) => element.id)), (element, layer) => {
    if (element.locked || layer.locked) return element;
    const position = positions.get(element.id);
    if (position === undefined) return element;
    const currentBounds = reportDesignerV3ElementBounds(element);
    const currentPosition = direction === "horizontal" ? currentBounds.left : currentBounds.top;
    const translation = position - currentPosition;
    return clampReportDesignerV3ElementToPage({
      ...element,
      ...(direction === "horizontal"
        ? { xHundredthMm: element.xHundredthMm + translation }
        : { yHundredthMm: element.yHundredthMm + translation }),
    }, state.schema.page);
  });
}

export function deleteSelectedV3Elements(state: ReportDesignerV3DocumentState): ReportDesignerV3DocumentState {
  const selected = new Set(state.selectedIds);
  if (selected.size === 0) return state;
  const layers = state.schema.layers.map((layer) => {
    if (layer.locked) return layer;
    const elements = layer.elements.filter((element) => !selected.has(element.id) || element.locked);
    return elements.length === layer.elements.length ? layer : { ...layer, elements };
  });
  if (layers.every((layer, index) => layer === state.schema.layers[index])) return state;
  const remaining = layers.flatMap((layer) => layer.elements);
  return {
    ...state,
    schema: { ...state.schema, layers },
    selectedIds: remaining[0] ? [remaining[0].id] : [],
  };
}

export function pasteV3Elements(
  state: ReportDesignerV3DocumentState,
  elements: ReportDesignerV3Element[],
  preferredLayerId?: string,
): ReportDesignerV3DocumentState {
  if (elements.length === 0) return state;
  const requestedCount = elements.length;
  if (countV3Elements(state.schema) + requestedCount > REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) return state;
  const fallbackLayer = state.schema.layers.find((l) => l.id === preferredLayerId && !l.locked)
    ?? state.schema.layers.find((l) => !l.locked);
  if (!fallbackLayer) return state;
  const existingIds = new Set([
    ...state.schema.layers.map((l) => l.id),
    ...state.schema.layers.flatMap((l) => l.elements.map((el) => el.id)),
  ]);
  const elementToLayerMap = new Map<string, string>();
  for (const layer of state.schema.layers) {
    for (const el of layer.elements) elementToLayerMap.set(el.id, layer.id);
  }
  const additionsByLayer = new Map<string, ReportDesignerV3Element[]>();
  const duplicates: ReportDesignerV3Element[] = [];
  for (const element of elements) {
    const origLayerId = elementToLayerMap.get(element.id);
    const targetLayer = (origLayerId ? state.schema.layers.find((l) => l.id === origLayerId && !l.locked) : null) ?? fallbackLayer;
    if (targetLayer.elements.length + (additionsByLayer.get(targetLayer.id)?.length ?? 0) >= REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER) {
      return state;
    }
    let id = createV3ElementId(element.type.toLowerCase());
    while (existingIds.has(id)) id = createV3ElementId(element.type.toLowerCase());
    existingIds.add(id);
    const maxZ = targetLayer.elements.reduce((max, item) => Math.max(max, item.zIndex), -1) + (additionsByLayer.get(targetLayer.id)?.length ?? 0) + 1;
    const cloned = clampReportDesignerV3ElementToPage({
      ...element,
      id,
      xHundredthMm: element.xHundredthMm + mmToHundredthMm(5),
      yHundredthMm: element.yHundredthMm + mmToHundredthMm(5),
      zIndex: maxZ,
      locked: false,
    } as ReportDesignerV3Element, state.schema.page);
    duplicates.push(cloned);
    const list = additionsByLayer.get(targetLayer.id) ?? [];
    list.push(cloned);
    additionsByLayer.set(targetLayer.id, list);
  }
  if (duplicates.length === 0) return state;
  const layers = state.schema.layers.map((layer) => {
    const adds = additionsByLayer.get(layer.id);
    return adds ? { ...layer, elements: [...layer.elements, ...adds] } : layer;
  });
  const firstTargetId = Array.from(additionsByLayer.keys())[0] ?? state.activeLayerId;
  return {
    ...state,
    schema: { ...state.schema, layers },
    selectedIds: duplicates.map((element) => element.id),
    activeLayerId: firstTargetId,
  };
}

export function duplicateSelectedV3Elements(state: ReportDesignerV3DocumentState): ReportDesignerV3DocumentState {
  const selected = new Set(state.selectedIds);
  const elements = state.schema.layers.flatMap((l) => l.locked ? [] : l.elements.filter((el) => selected.has(el.id) && !el.locked));
  return pasteV3Elements(state, elements);
}

export function setV3ElementZIndex(
  state: ReportDesignerV3DocumentState,
  elementId: string,
  direction: "front" | "back" | "forward" | "backward",
): ReportDesignerV3DocumentState {
  const located = findV3Element(state.schema, elementId);
  if (!located || located.element.locked || located.layer.locked) return state;
  // z-order is defined by the rendered zIndex, not by the persistence array
  // order.  Locked elements form hard barriers: an editable object may be
  // rearranged inside its segment, but can never cross (or renumber around)
  // a protected object.  This keeps a seal/background layer stable while
  // still allowing ordinary objects to be arranged freely.
  const elements = located.layer.elements
    .map((element, originalIndex) => ({ element, originalIndex }))
    .sort((left, right) => left.element.zIndex - right.element.zIndex || left.originalIndex - right.originalIndex)
    .map(({ element }) => element);
  const index = elements.findIndex((element) => element.id === elementId);
  if (index < 0) return state;
  let segmentStart = index;
  while (segmentStart > 0 && !elements[segmentStart - 1].locked) segmentStart -= 1;
  let segmentEnd = index;
  while (segmentEnd < elements.length - 1 && !elements[segmentEnd + 1].locked) segmentEnd += 1;
  const targetIndex = direction === "front"
    ? segmentEnd
    : direction === "back"
      ? segmentStart
      : direction === "forward"
        ? Math.min(segmentEnd, index + 1)
        : Math.max(segmentStart, index - 1);
  if (targetIndex === index) return state;
  const [moved] = elements.splice(index, 1);
  if (!moved) return state;
  elements.splice(targetIndex, 0, moved);
  const segment = elements.slice(segmentStart, segmentEnd + 1);
  const editableSlots = segment
    .filter((element) => !element.locked)
    .map((element) => element.zIndex)
    .sort((left, right) => left - right);
  const editableIds = new Set(segment.filter((element) => !element.locked).map((element) => element.id));
  let slotIndex = 0;
  const reordered = elements.map((element) => {
    if (!editableIds.has(element.id)) return element;
    const zIndex = editableSlots[slotIndex++];
    return zIndex === undefined ? element : { ...element, zIndex };
  });
  return {
    ...state,
    schema: {
      ...state.schema,
       layers: state.schema.layers.map((layer) => layer.id === located.layer.id ? { ...layer, elements: reordered } : layer),
    },
  };
}

export function toggleV3Selection(state: ReportDesignerV3DocumentState, elementId: string, additive: boolean): ReportDesignerV3DocumentState {
  const located = findV3Element(state.schema, elementId);
  if (!located) return state;
  if (!additive) {
    // A normal click selects the element, but clicking the sole selected
    // element again must not clear the selection.  Clearing is an explicit
    // canvas action (or Escape), while modifier-click remains the deliberate
    // selection toggle used for multi-select.
    return state.selectedIds.length === 1 && state.selectedIds[0] === elementId
      ? { ...state, activeLayerId: located.layer.id }
      : { ...state, selectedIds: [elementId], activeLayerId: located.layer.id };
  }

  const selected = new Set(state.selectedIds);
  if (selected.has(elementId)) selected.delete(elementId);
  else selected.add(elementId);
  return { ...state, selectedIds: Array.from(selected), activeLayerId: located.layer.id };
}

export function selectAllV3Elements(state: ReportDesignerV3DocumentState): ReportDesignerV3DocumentState {
  const activeLayer = state.schema.layers.find((layer) => layer.id === state.activeLayerId && layer.visible && !layer.locked);
  const targetLayers = activeLayer && activeLayer.elements.some((el) => el.visible && !el.locked)
    ? [activeLayer]
    : state.schema.layers.filter((layer) => layer.visible && !layer.locked);
  const selectableIds: string[] = [];
  for (const layer of targetLayers) {
    for (const el of layer.elements) if (el.visible && !el.locked) selectableIds.push(el.id);
  }
  return selectableIds.length > 0 ? { ...state, selectedIds: selectableIds } : state;
}

export function findV3Element(schema: ReportDesignerV3Schema, elementId: string) {
  for (const layer of schema.layers) {
    const element = layer.elements.find((candidate) => candidate.id === elementId);
    if (element) return { element, layer };
  }
  return null;
}

function updateV3Elements(
  state: ReportDesignerV3DocumentState,
  selected: Set<string>,
  update: (element: ReportDesignerV3Element, layer: ReportDesignerV3Layer) => ReportDesignerV3Element,
): ReportDesignerV3DocumentState {
  let changed = false;
  const layers = state.schema.layers.map((layer) => {
    const elements = layer.elements.map((element) => {
      if (!selected.has(element.id)) return element;
      const next = update(element, layer);
      if (!areV3ElementsEqual(element, next)) changed = true;
      return next;
    });
    return elements.some((element, index) => element !== layer.elements[index])
      ? { ...layer, elements }
      : layer;
  });
  if (!changed) return state;
  return {
    ...state,
    schema: {
      ...state.schema,
      layers,
    },
  };
}

function getMovableSelectedV3Elements(state: ReportDesignerV3DocumentState) {
  const selected = new Set(state.selectedIds);
  return state.schema.layers.flatMap((layer) => layer.elements
    .filter((element) => selected.has(element.id) && !element.locked && !layer.locked)
    .map((element) => ({ element, layer })));
}

function areV3ElementsEqual(left: ReportDesignerV3Element, right: ReportDesignerV3Element) {
  if (left === right) return true;
  const leftKeys = Object.keys(left) as Array<keyof ReportDesignerV3Element>;
  const rightKeys = Object.keys(right) as Array<keyof ReportDesignerV3Element>;
  if (leftKeys.length !== rightKeys.length) return false;
  for (const key of leftKeys) {
    if (key === "style") {
      if (!areStyleValuesEqual(left.style ?? {}, right.style ?? {})) return false;
      continue;
    }
    if (!Object.is(left[key], right[key])) return false;
  }
  return true;
}

function areStyleValuesEqual(
  left: ReportDesignerV3Element["style"] | undefined,
  right: ReportDesignerV3Element["style"] | undefined,
) {
  if (!left || !right) return left === right;
  const leftKeys = Object.keys(left) as Array<keyof ReportDesignerV3Element["style"]>;
  const rightKeys = Object.keys(right) as Array<keyof ReportDesignerV3Element["style"]>;
  if (leftKeys.length !== rightKeys.length) return false;
  return leftKeys.every((key) => Object.is(left[key], right[key]));
}

function normalizeMargin(value: number, maximum: number) {
  const parsed = Number.isFinite(value) ? Math.round(value) : 0;
  return Math.min(Math.max(0, maximum), Math.max(0, parsed));
}

export function collectV3ValidationIssues(state: ReportDesignerV3DocumentState): ReportDesignerSchemaIssue[] {
  const issues: ReportDesignerSchemaIssue[] = [];
  const selected = new Set(state.selectedIds);
  for (const id of selected) {
    if (!findV3Element(state.schema, id)) {
      issues.push({ severity: "warning", path: "$.selection", message: "选中的元素已不存在。" });
    }
  }
  const total = countV3Elements(state.schema);
  if (total >= REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) {
    issues.push({ severity: "warning", path: "$.layers", message: `已达到元素总数上限 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS}。` });
  }
  return issues;
}

export function getV3ElementCapacityIssue(
  state: ReportDesignerV3DocumentState,
  layerId?: string,
  requestedCount = 1,
) {
  if (!Number.isInteger(requestedCount) || requestedCount < 1) return null;
  const layer = layerId ? state.schema.layers.find((candidate) => candidate.id === layerId) : undefined;
  if (layer && layer.elements.length + requestedCount > REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER) {
    return `该图层最多 ${REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER} 个元素。`;
  }
  if (countV3Elements(state.schema) + requestedCount > REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) {
    return `模板最多 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS} 个元素。`;
  }
  return null;
}

function countV3Elements(schema: ReportDesignerV3Schema) {
  return schema.layers.reduce((count, layer) => count + layer.elements.length, 0);
}

function sameV3Page(left: ReportDesignerV3Page, right: ReportDesignerV3Page) {
  return (Object.keys(left) as Array<keyof ReportDesignerV3Page>).every((key) => Object.is(left[key], right[key]));
}

function sameV3LayerPrint(left: ReportDesignerV3Layer["print"], right: ReportDesignerV3Layer["print"]) {
  return left.repeatOnEveryPage === right.repeatOnEveryPage &&
    left.keepTogether === right.keepTogether &&
    left.pinToPageBottom === right.pinToPageBottom &&
    left.minHeightHundredthMm === right.minHeightHundredthMm;
}

function normalizeLayerPrintForMutation(
  value: ReportDesignerV3Layer["print"] | undefined,
  role: ReportDesignerV3Layer["role"],
): ReportDesignerV3Layer["print"] {
  const source = (value && typeof value === "object" ? value : {}) as Partial<ReportDesignerV3Layer["print"]>;
  const requestedHeight = Number(source.minHeightHundredthMm);
  const minHeightHundredthMm = Number.isFinite(requestedHeight)
    ? Math.min(26000, Math.max(0, Math.round(requestedHeight)))
    : 0;
  return {
    repeatOnEveryPage: role === "Body" ? false : source.repeatOnEveryPage === true,
    keepTogether: source.keepTogether === true,
    pinToPageBottom: role === "Footer" && source.pinToPageBottom === true,
    minHeightHundredthMm,
  };
}
