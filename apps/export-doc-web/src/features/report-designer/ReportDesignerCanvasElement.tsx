import type { CSSProperties, PointerEvent as ReactPointerEvent } from "react";
import { renderReportDesignerBlockPreviewToHtml } from "./reportDesignerBlockRenderer.ts";
import type { ReportDesignerV3ResizeDirection } from "./reportDesignerV3Mutations.ts";
import {
  hundredthMmToMm,
  type ReportDesignerV3Element,
} from "./reportDesignerV3Schema.ts";

export function ReportDesignerCanvasElementPreview({ element, selectedGridCellId }: {
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
          <div
            className="report-designer-v3-preview-flow-content"
            dangerouslySetInnerHTML={{ __html: renderReportDesignerBlockPreviewToHtml(element.block, selectedGridCellId) }}
          />
        </div>
      );
  }
}

export function ReportDesignerCanvasResizeHandles({
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
