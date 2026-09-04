import { useEffect, useMemo, useRef, type CSSProperties, type PointerEvent as ReactPointerEvent } from "react";
import type {
  ReportDesignerV3DocumentState,
  ReportDesignerV3ResizeDirection,
} from "./reportDesignerV3Mutations.ts";
import {
  createV3MoveConstraint,
  resolveV3MoveDeltaFromConstraint,
  type ReportDesignerV3MoveConstraint,
} from "./reportDesignerGeometry.ts";
import { resolveV3ResizeGeometry } from "./reportDesignerV3Resize.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  reportDesignerV3PageSize,
  type ReportDesignerV3Element,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import { fitReportDesignerV3Zoom } from "./reportDesignerV3WorkspaceHelpers.tsx";
import { reportDesignerLayerBandStyle } from "./reportDesignerLayerBands.ts";
import { ReportDesignerLayerResizers } from "./ReportDesignerLayerResizers.tsx";
import {
  ReportDesignerCanvasElementPreview,
  ReportDesignerCanvasResizeHandles,
  reportDesignerCanvasElementStyle,
} from "./ReportDesignerCanvasElement.tsx";
import {
  findReportDesignerElementNodes,
  prepareReportDesignerGestureNodes,
  readReportDesignerGridCellId,
  releaseReportDesignerGestureNodes,
} from "./reportDesignerCanvasGesture.ts";

export type ReportDesignerV3Transform =
  | { kind: "move" }
  | { kind: "resize"; elementId: string; direction: ReportDesignerV3ResizeDirection };

type Gesture = {
  pointerId: number;
  startX: number;
  startY: number;
  lastX: number;
  lastY: number;
  baseState: ReportDesignerV3DocumentState;
  transform: ReportDesignerV3Transform;
  captureTarget: HTMLElement | null;
  pendingAnimationFrame: number | null;
  baseElements: Map<string, ReportDesignerV3Element>;
  elementNodes: Map<string, HTMLElement>;
  moveConstraint: ReportDesignerV3MoveConstraint | null;
};

export function ReportDesignerV3Canvas({
  state,
  zoom,
  fitRequest = 0,
  showGuides = true,
  onFitZoom,
  disabled = false,
  onSelect,
  selectedGridCell,
  onSelectGridCell,
  onCommitTransform,
  onCancelTransform,
  onCommitLayerBand,
  onClearSelection,
  }: {
  state: ReportDesignerV3DocumentState;
  zoom: number;
  fitRequest?: number;
  showGuides?: boolean;
  onFitZoom?: (zoom: number) => void;
  disabled?: boolean;
  onSelect: (elementId: string, additive: boolean) => void;
  selectedGridCell?: { elementId: string; cellId: string } | null;
  onSelectGridCell?: (elementId: string, cellId: string) => void;
  onCommitTransform: (baseState: ReportDesignerV3DocumentState, transform: ReportDesignerV3Transform, deltaX: number, deltaY: number) => void;
  onCancelTransform: (baseState: ReportDesignerV3DocumentState) => void;
  onCommitLayerBand: (role: "Header" | "Footer", heightHundredthMm: number) => void;
  onClearSelection: () => void;
}) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const callbacksRef = useRef({ onCommitTransform, onCancelTransform });
  callbacksRef.current = { onCommitTransform, onCancelTransform };
  const page = reportDesignerV3PageSize(state.schema.page);
  const displayedWidthMm = page.widthMm * zoom;
  const displayedHeightMm = page.heightMm * zoom;
  const selectedSet = new Set(state.selectedIds);
  const statusHint = useMemo(() => {
    if (state.selectedIds.length === 1) {
      const found = state.schema.layers.flatMap((l) => l.elements).find((el) => el.id === state.selectedIds[0]);
      if (found) {
        const typeLabel = found.type === "Text" ? "文本" : found.type === "Field" ? "字段" : found.type === "Image" ? "图片" : found.type === "Rectangle" ? "矩形" : found.type === "Line" ? "线条" : found.type === "Flow" ? "结构流" : found.type;
        return `已选【${typeLabel}】X: ${hundredthMmToMm(found.xHundredthMm).toFixed(1)} mm, Y: ${hundredthMmToMm(found.yHundredthMm).toFixed(1)} mm, 宽: ${hundredthMmToMm(found.widthHundredthMm).toFixed(1)} mm, 高: ${hundredthMmToMm(found.heightHundredthMm).toFixed(1)} mm · 拖拽移动/角点缩放 · 方向键微移 · Ctrl+C 复制 · Del 删除`;
      }
    }
    if (state.selectedIds.length > 1) {
      return `已多选 ${state.selectedIds.length} 个元素 · 可在右侧属性栏批量对齐/分布/操作 · 方向键整体微移 · Ctrl+C 复制 · Del 删除`;
    }
    return "未选择元素 · 单击选中 · 按住 Ctrl/Shift 多选 · Ctrl+A 全选 · 拖动移动 · 拖拽角点缩放 · 单位: 毫米(mm)";
  }, [state.selectedIds, state.schema]);

  useEffect(() => {
    if (fitRequest <= 0 || !onFitZoom) return;
    const scroll = scrollRef.current;
    const canvas = canvasRef.current;
    if (!scroll || !canvas) return;
    const styles = getComputedStyle(scroll);
    const horizontalPadding = parseFloat(styles.paddingLeft) + parseFloat(styles.paddingRight);
    const verticalPadding = parseFloat(styles.paddingTop) + parseFloat(styles.paddingBottom);
    const viewportWidth = Math.max(0, scroll.clientWidth - horizontalPadding);
    const viewportHeight = Math.max(0, scroll.clientHeight - verticalPadding);
    onFitZoom(fitReportDesignerV3Zoom(viewportWidth, viewportHeight, canvas.offsetWidth, canvas.offsetHeight));
  }, [fitRequest, onFitZoom]);

  function beginMove(event: ReactPointerEvent<HTMLDivElement>, element: ReportDesignerV3Element, layerId: string) {
    if (event.button !== 0) return;
    event.stopPropagation();
    const additive = event.shiftKey || event.ctrlKey || event.metaKey;
    const alreadySelected = state.selectedIds.includes(element.id);
    const selectedIds = alreadySelected && !additive
      ? state.selectedIds
      : additive
        ? (alreadySelected ? state.selectedIds.filter((id) => id !== element.id) : [...state.selectedIds, element.id])
        : [element.id];
    const gridCellId = readReportDesignerGridCellId(event.target);
    if (gridCellId && element.type === "Flow" && element.flowKind === "Grid") onSelectGridCell?.(element.id, gridCellId);
    onSelect(element.id, additive);
    // A modifier click on an already selected element is a selection toggle,
    // not the start of a drag.  Starting a gesture here would preview a move
    // while simultaneously removing the element from the selection.
    if (additive && alreadySelected) return;
    if (disabled) return;
    const layer = state.schema.layers.find((candidate) => candidate.id === layerId);
    if (element.locked || layer?.locked) return;
    const baseState: ReportDesignerV3DocumentState = {
      ...state,
      selectedIds,
      activeLayerId: layerId,
    };
    const baseElements = baseElementsFor(state.schema, selectedIds, true);
    const elementNodes = findReportDesignerElementNodes(canvasRef.current, baseElements.keys());
    gesture.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      baseState,
      transform: { kind: "move" },
      captureTarget: event.currentTarget,
      pendingAnimationFrame: null,
      baseElements,
      elementNodes,
      moveConstraint: createV3MoveConstraint(state.schema, baseElements.values()),
    };
    prepareReportDesignerGestureNodes(elementNodes, "move");
    capturePointer(event.currentTarget, event.pointerId);
  }

  function beginResize(event: ReactPointerEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) {
    event.preventDefault();
    event.stopPropagation();
    if (disabled) return;
    const located = findElement(state.schema, elementId);
    if (!located || located.element.locked || located.layer.locked) return;
    const baseState: ReportDesignerV3DocumentState = {
      ...state,
      selectedIds: [elementId],
      activeLayerId: located.layer.id,
    };
    onSelect(elementId, false);
    const baseElements = baseElementsFor(state.schema, [elementId]);
    const elementNodes = findReportDesignerElementNodes(canvasRef.current, baseElements.keys());
    gesture.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      baseState,
      transform: { kind: "resize", elementId, direction },
      captureTarget: event.currentTarget,
      pendingAnimationFrame: null,
      baseElements,
      elementNodes,
      moveConstraint: null,
    };
    prepareReportDesignerGestureNodes(elementNodes, "resize");
    capturePointer(event.currentTarget, event.pointerId);
  }

  function updateGesture(event: ReactPointerEvent<HTMLElement>) { updateGestureAt(event.pointerId, event.clientX, event.clientY); }
  function finishGesture(event: ReactPointerEvent<HTMLElement>, cancelled = false) { finishGestureAt(event.pointerId, event.clientX, event.clientY, cancelled); }
  function handleLostPointerCapture(event: ReactPointerEvent<HTMLElement>) { if (gesture.current && gesture.current.pointerId === event.pointerId) { /* keep gesture alive */ } }

  function updateGestureAt(pointerId: number, clientX: number, clientY: number) {
    const current = gesture.current;
    if (!current || current.pointerId !== pointerId) return;
    current.lastX = Number.isFinite(clientX) ? clientX : current.lastX;
    current.lastY = Number.isFinite(clientY) ? clientY : current.lastY;
    schedulePreview(current);
  }

  function finishGestureAt(pointerId: number, clientX: number, clientY: number, cancelled = false) {
    const current = gesture.current;
    if (!current || current.pointerId !== pointerId) return;
    if (!cancelled) {
      // Pointer-up can be delivered without a final pointer-move (for example
      // when the pointer is released between browser sampling ticks).  Use the
      // terminal event coordinates so the committed transform never lags the
      // previewed/final pointer position.
      const finalX = Number.isFinite(clientX) ? clientX : current.lastX;
      const finalY = Number.isFinite(clientY) ? clientY : current.lastY;
      current.lastX = finalX;
      current.lastY = finalY;
      cancelScheduledPreview(current);
      const delta = readDelta(
        current.startX,
        current.startY,
        finalX,
        finalY,
        canvasRef.current,
        current.baseState.schema,
      );
      applyTransientTransform(current, delta.x, delta.y);
      callbacksRef.current.onCommitTransform(current.baseState, current.transform, delta.x, delta.y);
      if (current.transform.kind === "move") applyTransientTransform(current, 0, 0);
    } else {
      cancelScheduledPreview(current);
      applyTransientTransform(current, 0, 0);
      callbacksRef.current.onCancelTransform(current.baseState);
    }
    releaseReportDesignerGestureNodes(current.elementNodes);
    gesture.current = null;
    releasePointer(current.captureTarget, current.pointerId);
  }

  function cancelGesture() {
    const current = gesture.current;
    if (!current) return;
    cancelScheduledPreview(current);
    applyTransientTransform(current, 0, 0);
    callbacksRef.current.onCancelTransform(current.baseState);
    releaseReportDesignerGestureNodes(current.elementNodes);
    gesture.current = null;
    releasePointer(current.captureTarget, current.pointerId);
  }

  function schedulePreview(current: Gesture) {
    if (current.pendingAnimationFrame !== null) return;
    current.pendingAnimationFrame = requestAnimationFrame(() => {
      current.pendingAnimationFrame = null;
      const delta = readDelta(
        current.startX,
        current.startY,
        current.lastX,
        current.lastY,
        canvasRef.current,
        current.baseState.schema,
      );
      applyTransientTransform(current, delta.x, delta.y);
    });
  }

  function cancelScheduledPreview(current: Gesture) {
    if (current.pendingAnimationFrame === null) return;
    cancelAnimationFrame(current.pendingAnimationFrame);
    current.pendingAnimationFrame = null;
  }

  function applyTransientTransform(current: Gesture, deltaX: number, deltaY: number) {
    const move = current.transform.kind === "move"
      ? resolveV3MoveDeltaFromConstraint(current.moveConstraint, deltaX, deltaY, current.baseState.schema.grid.snap)
      : null;
    for (const [elementId, baseElement] of current.baseElements) {
      const node = current.elementNodes.get(elementId);
      if (!node) continue;
      if (move) {
        const rotation = baseElement.rotationDeg ? ` rotate(${baseElement.rotationDeg}deg)` : "";
        const translate = move.dx || move.dy
          ? `translate3d(${hundredthMmToMm(move.dx)}mm, ${hundredthMmToMm(move.dy)}mm, 0)`
          : "";
        node.style.transform = `${translate}${rotation}`.trim();
        continue;
      }
      const geometry = current.transform.kind === "resize"
        ? resolveV3ResizeGeometry(baseElement, current.transform.direction, deltaX, deltaY, current.baseState.schema.page)
        : baseElement;
      node.style.left = `${hundredthMmToMm(geometry.xHundredthMm)}mm`;
      node.style.top = `${hundredthMmToMm(geometry.yHundredthMm)}mm`;
      node.style.width = `${hundredthMmToMm(geometry.widthHundredthMm)}mm`;
      node.style.height = `${hundredthMmToMm(geometry.heightHundredthMm)}mm`;
      node.style.transform = geometry.rotationDeg ? `rotate(${geometry.rotationDeg}deg)` : "";
    }
  }

  useEffect(() => {
    const handleWindowPointerMove = (event: PointerEvent) => {
      const current = gesture.current;
      if (!current || current.pointerId !== event.pointerId) return;
      // React's page-level handler already receives captured events.  Only use
      // the window fallback after capture has actually been lost/rejected.
      if (current.captureTarget?.hasPointerCapture?.(event.pointerId)) return;
      updateGestureAt(event.pointerId, event.clientX, event.clientY);
    };
    const handleWindowPointerUp = (event: PointerEvent) => {
      const current = gesture.current;
      if (!current || current.pointerId !== event.pointerId) return;
      if (current.captureTarget?.hasPointerCapture?.(event.pointerId)) return;
      finishGestureAt(event.pointerId, event.clientX, event.clientY);
    };
    const handleWindowPointerCancel = (event: PointerEvent) => {
      const current = gesture.current;
      if (!current || current.pointerId !== event.pointerId) return;
      finishGestureAt(event.pointerId, event.clientX, event.clientY, true);
    };
    window.addEventListener("pointermove", handleWindowPointerMove);
    window.addEventListener("pointerup", handleWindowPointerUp);
    window.addEventListener("pointercancel", handleWindowPointerCancel);
    window.addEventListener("blur", cancelGesture);
    return () => {
      window.removeEventListener("pointermove", handleWindowPointerMove);
      window.removeEventListener("pointerup", handleWindowPointerUp);
      window.removeEventListener("pointercancel", handleWindowPointerCancel);
      window.removeEventListener("blur", cancelGesture);
      cancelGesture();
    };
  }, []);

  return (
    <div className="report-designer-v3-canvas-shell">
      <div
        className="report-designer-v3-canvas-scroll"
        ref={scrollRef}
        onPointerDown={(event) => {
          if (event.target === event.currentTarget) onClearSelection();
        }}
      >
        <div
          className="report-designer-v3-page-frame"
          style={{ width: `${displayedWidthMm}mm`, height: `${displayedHeightMm}mm`, "--v3-page-ratio": `${page.widthMm} / ${page.heightMm}` } as CSSProperties}
        >
          <div
            ref={canvasRef}
            className={`report-designer-v3-page${state.schema.grid.enabled ? "" : " is-grid-hidden"}${showGuides ? "" : " is-guides-hidden"}${disabled ? " is-read-only" : ""}`}
            data-v3-page-canvas="true"
            style={{
              width: `${page.widthMm}mm`,
              height: `${page.heightMm}mm`,
              "--v3-grid-size": `${hundredthMmToMm(state.schema.grid.sizeHundredthMm)}mm`,
              "--v3-page-ratio": `${page.widthMm} / ${page.heightMm}`,
              "--v3-zoom": zoom,
              "--v3-margin-top": `${hundredthMmToMm(state.schema.page.marginTopHundredthMm)}mm`,
              "--v3-margin-right": `${hundredthMmToMm(state.schema.page.marginRightHundredthMm)}mm`,
              "--v3-margin-bottom": `${hundredthMmToMm(state.schema.page.marginBottomHundredthMm)}mm`,
              "--v3-margin-left": `${hundredthMmToMm(state.schema.page.marginLeftHundredthMm)}mm`,
              ...reportDesignerLayerBandStyle(state.schema),
              transform: `scale(${zoom})`,
              transformOrigin: "top left",
            } as CSSProperties}
          onPointerMove={updateGesture}
          onPointerUp={(event) => finishGesture(event)}
          onPointerCancel={(event) => finishGesture(event, true)}
          onLostPointerCapture={handleLostPointerCapture}
          onPointerDown={(event) => {
            if (event.target === event.currentTarget || (event.target instanceof HTMLElement && event.target.classList.contains("report-designer-v3-layer"))) onClearSelection();
          }}
          aria-readonly={disabled || undefined}
          role="region"
          aria-label="v3 报表自由画布"
        >
          {state.schema.layers.filter((layer) => layer.visible).map((layer) => (
            <div className={`report-designer-v3-layer report-designer-v3-layer-${layer.role.toLowerCase()}${state.activeLayerId === layer.id ? " is-active" : ""}`} key={layer.id} data-v3-layer-id={layer.id} data-v3-layer-name={layer.name} data-v3-layer-role={layer.role} aria-label={layer.name} aria-current={state.activeLayerId === layer.id ? "true" : undefined} style={{ "--v3-layer-label-top": layer.role === "Body" ? "42%" : layer.role === "Footer" ? "calc(100% - 20px)" : "4px", "--v3-layer-label-left": layer.role === "Overlay" ? "auto" : "4px", "--v3-layer-label-right": layer.role === "Overlay" ? "4px" : "auto" } as CSSProperties}>
              {[...layer.elements]
                .filter((element) => element.visible)
                .sort((left, right) => left.zIndex - right.zIndex)
                .map((element) => {
                  const selected = selectedSet.has(element.id);
                  return (
                    <div
                      className={`report-designer-v3-element report-designer-v3-element-${element.type.toLowerCase()}${selected ? " is-selected" : ""}${element.locked || layer.locked ? " is-locked" : ""}`}
                      key={element.id}
                      style={reportDesignerCanvasElementStyle(element)}
                      data-v3-element-id={element.id}
                      onPointerDown={(event) => beginMove(event, element, layer.id)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          onSelect(element.id, event.shiftKey || event.ctrlKey || event.metaKey);
                        }
                      }}
                      title={`${reportDesignerV3ElementText(element)}${element.locked ? "（已锁定）" : ""}`}
                      role="button"
                      tabIndex={0}
                      aria-pressed={selected}
                      aria-disabled={element.locked || layer.locked}
                      aria-label={`${reportDesignerV3ElementText(element)}${element.locked ? "，已锁定" : ""}`}
                    >
                      <ReportDesignerCanvasElementPreview element={element} selectedGridCellId={selectedGridCell?.elementId === element.id ? selectedGridCell.cellId : undefined} />
                      {selected && state.selectedIds.length === 1 && !element.locked && !layer.locked ? (
                        <ReportDesignerCanvasResizeHandles elementId={element.id} onPointerDown={beginResize} />
                      ) : null}
                      {selected && (element.locked || layer.locked) ? <span className="report-designer-v3-lock-badge">锁</span> : null}
                    </div>
                  );
                })}
            </div>
          ))}
          {showGuides ? <ReportDesignerLayerResizers schema={state.schema} disabled={disabled} onCommit={onCommitLayerBand} /> : null}
          </div>
        </div>
      </div>
      <div className="report-designer-v3-canvas-hint">{statusHint}</div>
    </div>
  );
}

function findElement(schema: ReportDesignerV3Schema, id: string) {
  for (const layer of schema.layers) {
    const element = layer.elements.find((candidate) => candidate.id === id);
    if (element) return { element, layer };
  }
  return null;
}

function baseElementsFor(schema: ReportDesignerV3Schema, ids: string[], movableOnly = false) {
  const wanted = new Set(ids);
  const result = new Map<string, ReportDesignerV3Element>();
  for (const layer of schema.layers) {
    for (const element of layer.elements) {
      if (wanted.has(element.id) && (!movableOnly || (!element.locked && !layer.locked))) result.set(element.id, element);
    }
  }
  return result;
}

function readDelta(
  startX: number,
  startY: number,
  currentX: number,
  currentY: number,
  canvas: HTMLDivElement | null,
  schema: ReportDesignerV3Schema,
) {
  const rect = canvas?.getBoundingClientRect();
  if (!rect || rect.width <= 0 || rect.height <= 0) return { x: 0, y: 0 };
  return {
    x: Math.round(((currentX - startX) / rect.width) * schema.page.widthHundredthMm),
    y: Math.round(((currentY - startY) / rect.height) * schema.page.heightHundredthMm),
  };
}

function capturePointer(target: HTMLElement, pointerId: number) {
  try {
    target.setPointerCapture(pointerId);
  } catch {
    // Some embedded WebViews can reject capture after a synthetic pointer
    // event.  The gesture remains cancellable and the page-level handlers
    // still provide a best-effort fallback.
  }
}

function releasePointer(target: HTMLElement | null, pointerId: number) {
  if (!target) return;
  try {
    if (target.hasPointerCapture(pointerId)) target.releasePointerCapture(pointerId);
  } catch {
    // The browser may have released capture already.
  }
}
