import { useEffect, useRef, type CSSProperties, type PointerEvent as ReactPointerEvent } from "react";
import type {
  ReportDesignerV3DocumentState,
  ReportDesignerV3ResizeDirection,
} from "./reportDesignerV3Mutations.ts";
import { moveSelectedV3Elements, resizeV3Element } from "./reportDesignerV3Mutations.ts";
import { renderReportDesignerBlockPreviewToHtml } from "./reportDesignerBlockRenderer.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  reportDesignerV3PageSize,
  type ReportDesignerV3Element,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";

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
};

export function ReportDesignerV3Canvas({
  state,
  zoom,
  disabled = false,
  onSelect,
  onCommitTransform,
  onCancelTransform,
  onClearSelection,
  }: {
    state: ReportDesignerV3DocumentState;
  zoom: number;
  disabled?: boolean;
  onSelect: (elementId: string, additive: boolean) => void;
  onCommitTransform: (baseState: ReportDesignerV3DocumentState, transform: ReportDesignerV3Transform, deltaX: number, deltaY: number) => void;
  onCancelTransform: (baseState: ReportDesignerV3DocumentState) => void;
  onClearSelection: () => void;
}) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const callbacksRef = useRef({ onCommitTransform, onCancelTransform });
  callbacksRef.current = { onCommitTransform, onCancelTransform };
  const page = reportDesignerV3PageSize(state.schema.page);
  const displayedWidthMm = page.widthMm * zoom;
  const displayedHeightMm = page.heightMm * zoom;
  const selectedSet = new Set(state.selectedIds);

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
    };
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
    };
    capturePointer(event.currentTarget, event.pointerId);
  }

  function updateGesture(event: ReactPointerEvent<HTMLElement>) {
    updateGestureAt(event.pointerId, event.clientX, event.clientY);
  }

  function finishGesture(event: ReactPointerEvent<HTMLElement>, cancelled = false) {
    finishGestureAt(event.pointerId, event.clientX, event.clientY, cancelled);
  }

  function handleLostPointerCapture(event: ReactPointerEvent<HTMLElement>) {
    const current = gesture.current;
    if (!current || current.pointerId !== event.pointerId) return;
    // Keep the gesture alive after capture is lost.  A window-level pointerup
    // or pointercancel listener below will finish it even when an embedded
    // WebView retargets the terminal event outside the canvas.
  }

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
      applyTransientTransform(current.baseState, current.transform, delta.x, delta.y);
      callbacksRef.current.onCommitTransform(current.baseState, current.transform, delta.x, delta.y);
    } else {
      cancelScheduledPreview(current);
      applyTransientTransform(current.baseState, current.transform, 0, 0);
      callbacksRef.current.onCancelTransform(current.baseState);
    }
    gesture.current = null;
    releasePointer(current.captureTarget, current.pointerId);
  }

  function cancelGesture() {
    const current = gesture.current;
    if (!current) return;
    cancelScheduledPreview(current);
    applyTransientTransform(current.baseState, current.transform, 0, 0);
    callbacksRef.current.onCancelTransform(current.baseState);
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
      applyTransientTransform(current.baseState, current.transform, delta.x, delta.y);
    });
  }

  function cancelScheduledPreview(current: Gesture) {
    if (current.pendingAnimationFrame === null) return;
    cancelAnimationFrame(current.pendingAnimationFrame);
    current.pendingAnimationFrame = null;
  }

  function applyTransientTransform(
    baseState: ReportDesignerV3DocumentState,
    transform: ReportDesignerV3Transform,
    deltaX: number,
    deltaY: number,
  ) {
    const next = transform.kind === "move"
      ? moveSelectedV3Elements(baseState, deltaX, deltaY)
      : resizeV3Element(baseState, transform.elementId, transform.direction, deltaX, deltaY);
    const page = canvasRef.current;
    if (!page) return;
    for (const elementId of baseState.selectedIds) {
      const located = findElement(next.schema, elementId);
      const node = page.querySelector<HTMLElement>(`[data-v3-element-id="${CSS.escape(elementId)}"]`);
      if (!located || !node) continue;
      node.style.left = `${hundredthMmToMm(located.element.xHundredthMm)}mm`;
      node.style.top = `${hundredthMmToMm(located.element.yHundredthMm)}mm`;
      node.style.width = `${hundredthMmToMm(located.element.widthHundredthMm)}mm`;
      node.style.height = `${hundredthMmToMm(located.element.heightHundredthMm)}mm`;
      node.style.transform = located.element.rotationDeg ? `rotate(${located.element.rotationDeg}deg)` : "";
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
        onPointerDown={(event) => {
          if (event.target === event.currentTarget) onClearSelection();
        }}
      >
        <div
          className="report-designer-v3-page-frame"
          style={{ width: `${displayedWidthMm}mm`, height: `${displayedHeightMm}mm` }}
        >
          <div
            ref={canvasRef}
            className={`report-designer-v3-page${state.schema.grid.enabled ? "" : " is-grid-hidden"}`}
            style={{
              width: `${page.widthMm}mm`,
              height: `${page.heightMm}mm`,
              "--v3-grid-size": `${hundredthMmToMm(state.schema.grid.sizeHundredthMm)}mm`,
              "--v3-zoom": zoom,
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
          role="region"
          aria-label="v3 报表自由画布"
        >
          {state.schema.layers.filter((layer) => layer.visible).map((layer) => (
            <div className={`report-designer-v3-layer report-designer-v3-layer-${layer.role.toLowerCase()}`} key={layer.id}>
              {[...layer.elements]
                .filter((element) => element.visible)
                .sort((left, right) => left.zIndex - right.zIndex)
                .map((element) => {
                  const selected = selectedSet.has(element.id);
                  return (
                    <div
                      className={`report-designer-v3-element report-designer-v3-element-${element.type.toLowerCase()}${selected ? " is-selected" : ""}${element.locked || layer.locked ? " is-locked" : ""}`}
                      key={element.id}
                      style={elementStyle(element)}
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
                      <ElementPreview element={element} />
                      {selected && state.selectedIds.length === 1 && !element.locked && !layer.locked ? (
                        <ResizeHandles elementId={element.id} onPointerDown={beginResize} />
                      ) : null}
                      {selected && (element.locked || layer.locked) ? <span className="report-designer-v3-lock-badge">锁</span> : null}
                    </div>
                  );
                })}
            </div>
          ))}
          </div>
        </div>
      </div>
      <div className="report-designer-v3-canvas-hint">拖动元素移动；拖拽角点缩放；Shift/Ctrl/⌘ 可多选；坐标单位为毫米。</div>
    </div>
  );
}

function ElementPreview({ element }: { element: ReportDesignerV3Element }) {
  switch (element.type) {
    case "Text":
      return <span className="report-designer-v3-preview-text">{element.text || "文本"}</span>;
    case "Field":
      return (
        <span className="report-designer-v3-preview-field">
          {element.label ? `${element.label}: ` : ""}
          {`{{ ${element.fieldPath || "字段"} }}`}
        </span>
      );
    case "Image":
      return <span className="report-designer-v3-preview-image">{element.sourceKind === "Field" ? `图片：${element.fieldPath ?? ""}` : element.resourceId ? `资源：${element.resourceId}` : "图片资源未上传"}</span>;
    case "Rectangle":
      return null;
    case "Line":
      return null;
    case "Flow":
      return (
        <div className="report-designer-v3-preview-flow" aria-label={`${element.flowKind} 结构预览`}>
          <div
            className="report-designer-v3-preview-flow-content"
            dangerouslySetInnerHTML={{ __html: renderReportDesignerBlockPreviewToHtml(element.block) }}
          />
        </div>
      );
  }
}

function ResizeHandles({
  elementId,
  onPointerDown,
}: {
  elementId: string;
  onPointerDown: (event: ReactPointerEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) => void;
}) {
  const directions: ReportDesignerV3ResizeDirection[] = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
  return (
    <>
      {directions.map((direction) => (
        <button
          className={`report-designer-v3-handle report-designer-v3-handle-${direction}`}
          key={direction}
          type="button"
          aria-label={`调整大小 ${direction}`}
          onPointerDown={(event) => onPointerDown(event, elementId, direction)}
        />
      ))}
    </>
  );
}

function elementStyle(element: ReportDesignerV3Element): CSSProperties {
  const style: CSSProperties = {
    left: `${hundredthMmToMm(element.xHundredthMm)}mm`,
    top: `${hundredthMmToMm(element.yHundredthMm)}mm`,
    width: `${hundredthMmToMm(element.widthHundredthMm)}mm`,
    height: `${hundredthMmToMm(element.heightHundredthMm)}mm`,
    zIndex: element.zIndex,
    transform: element.rotationDeg ? `rotate(${element.rotationDeg}deg)` : undefined,
    fontFamily: element.style.fontFamily,
    fontSize: element.style.fontSizePt ? `${element.style.fontSizePt}pt` : undefined,
    fontWeight: element.style.bold ? 700 : undefined,
    color: element.style.color,
    backgroundColor: element.style.backgroundColor,
    textAlign: element.style.align?.toLowerCase() as CSSProperties["textAlign"],
    borderColor: element.style.borderColor,
    borderWidth: element.style.borderWidthPx,
    borderStyle: element.style.borderStyle === "Dashed" ? "dashed" : element.style.borderStyle === "None" ? "none" : element.style.borderWidthPx ? "solid" : undefined,
    padding: element.style.paddingHundredthMm ? `${hundredthMmToMm(element.style.paddingHundredthMm)}mm` : undefined,
  };
  if (element.type === "Line") {
    style.border = "0";
    style.backgroundColor = element.style.borderColor ?? "#334155";
  }
  return style;
}

function findElement(schema: ReportDesignerV3Schema, id: string) {
  for (const layer of schema.layers) {
    const element = layer.elements.find((candidate) => candidate.id === id);
    if (element) return { element, layer };
  }
  return null;
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
