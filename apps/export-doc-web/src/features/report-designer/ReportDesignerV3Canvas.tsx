import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from "react";
import { getGridCellLocations } from "./reportDesignerGridMutations.ts";
import { ReportDesignerCanvasTextEditor, type ReportDesignerTextEdit } from "./ReportDesignerCanvasTextEditor.tsx";
import {
  findV3Element,
  type ReportDesignerV3DocumentState,
  type ReportDesignerV3ResizeDirection,
} from "./reportDesignerV3Mutations.ts";
import {
  resolveV3MoveDeltaFromConstraint,
  type ReportDesignerV3MoveConstraint,
} from "./reportDesignerGeometry.ts";
import { createV3RegionMoveConstraint } from "./reportDesignerV3Regions.ts";
import { resolveV3ResizeGeometry } from "./reportDesignerV3Resize.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  reportDesignerV3ElementKindLabel,
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
  coordinateScale: { x: number; y: number };
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
  onCommitText,
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
  onCommitText: (elementId: string, cellId: string | undefined, text: string) => void;
}) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const [textEdit, setTextEdit] = useState<ReportDesignerTextEdit | null>(null);
  const callbacksRef = useRef({ onCommitTransform, onCancelTransform, schema: state.schema });
  callbacksRef.current = { onCommitTransform, onCancelTransform, schema: state.schema };
  const page = reportDesignerV3PageSize(state.schema.page);
  const displayedWidthMm = page.widthMm * zoom;
  const displayedHeightMm = page.heightMm * zoom;
  const selectedSet = new Set(state.selectedIds);
  const statusHint = useMemo(() => {
    if (state.selectedIds.length === 1) {
      const found = state.schema.layers.flatMap((l) => l.elements).find((el) => el.id === state.selectedIds[0]);
      if (found) {
        const typeLabel = reportDesignerV3ElementKindLabel(found);
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
    if (disabled) return;
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
    const layer = state.schema.layers.find((candidate) => candidate.id === layerId);
    if (element.locked || layer?.locked) return;
    const baseState: ReportDesignerV3DocumentState = {
      ...state,
      selectedIds,
      activeLayerId: layerId,
    };
    const baseElements = baseElementsFor(state.schema, selectedIds, true);
    const coordinateScale = readCoordinateScale(canvasRef.current, state.schema);
    if (!coordinateScale) return;
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
      moveConstraint: createV3RegionMoveConstraint(state.schema, baseElements.values()),
      coordinateScale,
    };
    prepareReportDesignerGestureNodes(elementNodes, "move");
    capturePointer(event.currentTarget, event.pointerId);
  }

  function beginTextEdit(target: EventTarget | null, element: ReportDesignerV3Element) {
    const located = findV3Element(state.schema, element.id);
    const canvas = canvasRef.current;
    if (disabled || !canvas || !located || element.locked || located.layer.locked) return;
    const cellId = readReportDesignerGridCellId(target) ??
      (selectedGridCell?.elementId === element.id ? selectedGridCell.cellId : undefined);
    const cell = element.type === "Flow" && element.block.type === "Grid" && cellId
      ? getGridCellLocations(element.block).find((location) => location.cell.id === cellId)?.cell : undefined;
    const text = element.type === "Text" ? element.text : cell?.contentKind === "Text" ? cell.text : undefined;
    if (text === undefined || !(target instanceof Element)) return;
    const elementNode = target.closest("[data-v3-element-id]");
    const node = cellId ? elementNode?.querySelector(`[data-report-grid-cell-id="${CSS.escape(cellId)}"]`) : elementNode;
    if (!node) return;
    const rect = node.getBoundingClientRect();
    const pageRect = canvas.getBoundingClientRect();
    const scale = pageRect.width / canvas.offsetWidth;
    const width = Math.min(canvas.offsetWidth, Math.max(rect.width / scale, 140 / zoom));
    const height = Math.min(canvas.offsetHeight, Math.max(rect.height / scale, 44 / zoom));
    setTextEdit({ elementId: element.id, cellId, text, width, height,
      left: Math.max(0, Math.min((rect.left - pageRect.left) / scale, canvas.offsetWidth - width)),
      top: Math.max(0, Math.min((rect.top - pageRect.top) / scale, canvas.offsetHeight - height)),
    });
  }

  function finishTextEdit(text?: string) {
    if (!textEdit) return;
    const edit = textEdit;
    setTextEdit(null);
    if (text !== undefined && text !== edit.text) onCommitText(edit.elementId, edit.cellId, text);
    requestAnimationFrame(() => {
      findReportDesignerElementNodes(canvasRef.current, [edit.elementId]).get(edit.elementId)?.focus({ preventScroll: true });
    });
  }

  function beginResize(event: ReactPointerEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) {
    event.preventDefault();
    event.stopPropagation();
    if (disabled) return;
    const located = findV3Element(state.schema, elementId);
    if (!located || located.element.locked || located.layer.locked) return;
    const baseState: ReportDesignerV3DocumentState = {
      ...state,
      selectedIds: [elementId],
      activeLayerId: located.layer.id,
    };
    onSelect(elementId, false);
    const baseElements = baseElementsFor(state.schema, [elementId]);
    const coordinateScale = readCoordinateScale(canvasRef.current, state.schema);
    if (!coordinateScale) return;
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
      coordinateScale,
    };
    prepareReportDesignerGestureNodes(elementNodes, "resize");
    capturePointer(event.currentTarget, event.pointerId);
  }

  function updateGesture(event: ReactPointerEvent<HTMLElement>) { updateGestureAt(event.pointerId, event.clientX, event.clientY); }
  function resizeByKeyboard(event: ReactKeyboardEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) {
    const step = event.shiftKey ? 1000 : 100;
    const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    if (!dx && !dy) return;
    event.preventDefault();
    event.stopPropagation();
    if (!disabled) callbacksRef.current.onCommitTransform(state, { kind: "resize", elementId, direction }, dx, dy);
  }
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
        current.coordinateScale,
      );
      applyTransientTransform(current, delta.x, delta.y);
      callbacksRef.current.onCommitTransform(current.baseState, current.transform, delta.x, delta.y);
      restoreCommittedGeometry(current);
    } else {
      cancelScheduledPreview(current);
      restoreCommittedGeometry(current);
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
    restoreCommittedGeometry(current);
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
        current.coordinateScale,
      );
      applyTransientTransform(current, delta.x, delta.y);
    });
  }

  function cancelScheduledPreview(current: Gesture) {
    if (current.pendingAnimationFrame === null) return;
    cancelAnimationFrame(current.pendingAnimationFrame);
    current.pendingAnimationFrame = null;
  }

  function restoreCommittedGeometry(current: Gesture) {
    const elements = baseElementsFor(callbacksRef.current.schema, [...current.baseElements.keys()]);
    for (const [id, element] of elements) {
      const node = current.elementNodes.get(id);
      if (node) paintGeometry(node, element);
    }
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
      paintGeometry(node, geometry);
    }
  }

  useEffect(() => {
    cancelGesture();
  }, [zoom]);

  useLayoutEffect(() => {
    if (gesture.current && gesture.current.baseState.schema !== state.schema) cancelGesture();
  }, [state.schema]);

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
    window.addEventListener("resize", cancelGesture);
    return () => {
      window.removeEventListener("pointermove", handleWindowPointerMove);
      window.removeEventListener("pointerup", handleWindowPointerUp);
      window.removeEventListener("pointercancel", handleWindowPointerCancel);
      window.removeEventListener("blur", cancelGesture);
      window.removeEventListener("resize", cancelGesture);
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
            if (!disabled && (event.target === event.currentTarget || (event.target instanceof HTMLElement && event.target.classList.contains("report-designer-v3-layer")))) onClearSelection();
          }}
          aria-readonly={disabled || undefined}
          role="region"
          aria-label="v3 报表自由画布"
        >
          {state.schema.layers.filter((layer) => layer.visible).map((layer) => (
            <div className={`report-designer-v3-layer report-designer-v3-layer-${layer.role.toLowerCase()}${state.activeLayerId === layer.id ? " is-active" : ""}`} key={layer.id} data-v3-layer-id={layer.id} data-v3-layer-name={layer.name} data-v3-layer-role={layer.role} role="group" aria-label={layer.name} aria-current={state.activeLayerId === layer.id ? "true" : undefined}>
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
                      onDoubleClick={(event) => { event.stopPropagation(); beginTextEdit(event.target, element); }}
                      onKeyDown={(event) => {
                        if (event.target !== event.currentTarget) return;
                        if (!disabled && event.key === "F2") { event.preventDefault(); beginTextEdit(event.currentTarget, element); }
                        if (!disabled && (event.key === "Enter" || event.key === " ")) {
                          event.preventDefault();
                          onSelect(element.id, event.shiftKey || event.ctrlKey || event.metaKey);
                        }
                      }}
                      title={`${reportDesignerV3ElementText(element)}${element.locked ? "（已锁定）" : ""}`}
                      role={disabled ? "img" : "group"}
                      aria-roledescription={disabled ? undefined : "可移动画布组件"}
                      tabIndex={disabled ? -1 : 0}
                      aria-current={!disabled && selected ? "true" : undefined}
                      aria-disabled={!disabled && (element.locked || layer.locked) || undefined}
                      aria-label={`${reportDesignerV3ElementText(element)}${element.locked ? "，已锁定" : ""}`}
                    >
                      <ReportDesignerCanvasElementPreview element={element} selectedGridCellId={selectedGridCell?.elementId === element.id ? selectedGridCell.cellId : undefined} />
                      {!disabled && selected && state.selectedIds.length === 1 && !element.locked && !layer.locked ? (
                        <ReportDesignerCanvasResizeHandles elementId={element.id} onPointerDown={beginResize} onKeyDown={resizeByKeyboard} />
                      ) : null}
                      {selected && (element.locked || layer.locked) ? <span className="report-designer-v3-lock-badge">锁</span> : null}
                    </div>
                  );
                })}
            </div>
          ))}
          {showGuides ? <ReportDesignerLayerResizers schema={state.schema} disabled={disabled} onCommit={onCommitLayerBand} /> : null}
          {textEdit ? <ReportDesignerCanvasTextEditor key={`${textEdit.elementId}:${textEdit.cellId ?? ""}`} edit={textEdit} zoom={zoom} onCancel={() => finishTextEdit()} onCommit={finishTextEdit} /> : null}
          </div>
        </div>
      </div>
      <div className="report-designer-v3-canvas-hint">{statusHint}</div>
    </div>
  );
}

function paintGeometry(node: HTMLElement, geometry: Pick<ReportDesignerV3Element, "xHundredthMm" | "yHundredthMm" | "widthHundredthMm" | "heightHundredthMm" | "rotationDeg">) {
  node.style.left = `${hundredthMmToMm(geometry.xHundredthMm)}mm`;
  node.style.top = `${hundredthMmToMm(geometry.yHundredthMm)}mm`;
  node.style.width = `${hundredthMmToMm(geometry.widthHundredthMm)}mm`;
  node.style.height = `${hundredthMmToMm(geometry.heightHundredthMm)}mm`;
  node.style.transform = geometry.rotationDeg ? `rotate(${geometry.rotationDeg}deg)` : "";
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

function readCoordinateScale(canvas: HTMLDivElement | null, schema: ReportDesignerV3Schema) {
  const rect = canvas?.getBoundingClientRect();
  if (!rect || rect.width <= 0 || rect.height <= 0) return null;
  return { x: schema.page.widthHundredthMm / rect.width, y: schema.page.heightHundredthMm / rect.height };
}

function readDelta(
  startX: number,
  startY: number,
  currentX: number,
  currentY: number,
  scale: { x: number; y: number },
) {
  return {
    x: Math.round((currentX - startX) * scale.x),
    y: Math.round((currentY - startY) * scale.y),
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
