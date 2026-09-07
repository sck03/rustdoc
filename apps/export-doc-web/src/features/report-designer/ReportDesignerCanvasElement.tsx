import { memo, useLayoutEffect, useMemo, useRef, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from "react";
import type { ReportBlock } from "./reportDesignerSchema.ts";
import { renderReportDesignerBlockPreviewToHtml } from "./reportDesignerBlockRenderer.ts";
import type { ReportDesignerV3ResizeDirection } from "./reportDesignerV3Mutations.ts";
import {
  hundredthMmToMm,
  type ReportDesignerV3Element,
} from "./reportDesignerV3Schema.ts";

export const ReportDesignerCanvasElementPreview = memo(function ReportDesignerCanvasElementPreview({ element, selectedGridCellId }: {
  element: ReportDesignerV3Element;
  selectedGridCellId?: string;
}) {
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
    case "PageNumber":
      return <span className="report-designer-v3-preview-page-number">{element.prefix ?? ""}第 1 / 1 页{element.suffix ?? ""}</span>;
    case "Rectangle":
      return null;
    case "Line":
      return <span className={`report-designer-v3-preview-line report-designer-v3-preview-line-${element.direction.toLowerCase()}`} style={{ backgroundColor: element.style.borderColor ?? "var(--edm-neutral-700)", ...(element.direction === "Horizontal" ? { height: `${Math.max(1, element.style.borderWidthPx ?? 1)}px` } : { width: `${Math.max(1, element.style.borderWidthPx ?? 1)}px` }) }} aria-hidden="true" />;
    case "Flow":
      return (
        <div className="report-designer-v3-preview-flow" aria-label={`${element.flowKind} 结构预览`}>
          <FlowPreview block={element.block} selectedCellId={selectedGridCellId} />
        </div>
      );
  }
});

function FlowPreview({ block, selectedCellId }: { block: ReportBlock; selectedCellId?: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const html = useMemo(() => ({ __html: renderReportDesignerBlockPreviewToHtml(block) }), [block]);
  useLayoutEffect(() => {
    if (!selectedCellId) return;
    const cell = ref.current?.querySelector(`[data-report-grid-cell-id="${CSS.escape(selectedCellId)}"]`);
    cell?.classList.add("is-designer-selected-cell");
    return () => cell?.classList.remove("is-designer-selected-cell");
  }, [html, selectedCellId]);
  return <div ref={ref} className="report-designer-v3-preview-flow-content" dangerouslySetInnerHTML={html} />;
}

export function ReportDesignerCanvasResizeHandles({
  elementId,
  onPointerDown,
  onKeyDown,
}: {
  elementId: string;
  onPointerDown: (event: ReactPointerEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLButtonElement>, elementId: string, direction: ReportDesignerV3ResizeDirection) => void;
}) {
  const directions: ReportDesignerV3ResizeDirection[] = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
  return (
    <>
      {directions.map((direction) => (
        <button
          className={`report-designer-v3-handle report-designer-v3-handle-${direction}`}
          key={direction}
          type="button"
          aria-label={`调整${({ nw: "左上角", n: "上边", ne: "右上角", e: "右边", se: "右下角", s: "下边", sw: "左下角", w: "左边" })[direction]}尺寸`}
          title="拖动调整尺寸，也可使用方向键；Shift 加大步长"
          onPointerDown={(event) => onPointerDown(event, elementId, direction)}
          onKeyDown={(event) => onKeyDown(event, elementId, direction)}
        />
      ))}
    </>
  );
}

export function reportDesignerCanvasElementStyle(element: ReportDesignerV3Element): CSSProperties {
  const usesOuterStyle = element.type !== "Flow";
  const style: CSSProperties = {
    left: `${hundredthMmToMm(element.xHundredthMm)}mm`,
    top: `${hundredthMmToMm(element.yHundredthMm)}mm`,
    width: `${hundredthMmToMm(element.widthHundredthMm)}mm`,
    height: `${hundredthMmToMm(element.heightHundredthMm)}mm`,
    zIndex: element.zIndex,
    transform: element.rotationDeg ? `rotate(${element.rotationDeg}deg)` : undefined,
    fontFamily: usesOuterStyle ? element.style.fontFamily : undefined,
    fontSize: usesOuterStyle && element.style.fontSizePt ? `${element.style.fontSizePt}pt` : undefined,
    fontWeight: usesOuterStyle && element.style.bold ? 700 : undefined,
    color: usesOuterStyle ? element.style.color : undefined,
    backgroundColor: usesOuterStyle ? element.style.backgroundColor : undefined,
    textAlign: usesOuterStyle ? element.style.align?.toLowerCase() as CSSProperties["textAlign"] : undefined,
    borderColor: usesOuterStyle ? element.style.borderColor : undefined,
    borderWidth: usesOuterStyle && element.type !== "Line" ? element.style.borderWidthPx : undefined,
    borderStyle: usesOuterStyle && element.type !== "Line" ? element.style.borderStyle === "Dashed" ? "dashed" : element.style.borderStyle === "None" ? "none" : element.style.borderWidthPx ? "solid" : undefined : undefined,
    padding: usesOuterStyle && element.style.paddingHundredthMm ? `${hundredthMmToMm(element.style.paddingHundredthMm)}mm` : undefined,
  };
  if (element.type === "Line") style.backgroundColor = "transparent";
  return style;
}
