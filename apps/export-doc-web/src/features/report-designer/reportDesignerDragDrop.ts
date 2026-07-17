import type { DragEvent } from "react";

export type ReportDesignerPaletteComponentType =
  | "Text"
  | "Row"
  | "Grid"
  | "Conditional"
  | "Image"
  | "DetailTable"
  | "PageBreak";

export type ReportDesignerDragPayload =
  | {
      kind: "Block";
      blockId: string;
    }
  | {
      kind: "Field";
      label: string;
      value: string;
    }
  | {
      kind: "Component";
      componentType: ReportDesignerPaletteComponentType;
    };

const reportDesignerDragMime = "application/x-exportdoc-report-designer";

export function writeReportDesignerDragPayload(
  event: DragEvent<HTMLElement>,
  payload: ReportDesignerDragPayload,
) {
  event.dataTransfer.setData(reportDesignerDragMime, JSON.stringify(payload));
  event.dataTransfer.effectAllowed = payload.kind === "Block" ? "move" : "copy";
}

export function readReportDesignerDragPayload(
  event: DragEvent<HTMLElement>,
): ReportDesignerDragPayload | null {
  const rawPayload = event.dataTransfer.getData(reportDesignerDragMime);
  if (!rawPayload) {
    return null;
  }

  try {
    const payload = JSON.parse(rawPayload) as Partial<ReportDesignerDragPayload>;
    if (payload.kind === "Block" && typeof payload.blockId === "string") {
      return {
        kind: "Block",
        blockId: payload.blockId,
      };
    }

    if (payload.kind === "Field" && typeof payload.value === "string") {
      return {
        kind: "Field",
        label: typeof payload.label === "string" ? payload.label : payload.value,
        value: payload.value,
      };
    }

    if (payload.kind === "Component" && isPaletteComponentType(payload.componentType)) {
      return {
        kind: "Component",
        componentType: payload.componentType,
      };
    }
  } catch {
    return null;
  }

  return null;
}

function isPaletteComponentType(value: unknown): value is ReportDesignerPaletteComponentType {
  return value === "Text" ||
    value === "Row" ||
    value === "Grid" ||
    value === "Conditional" ||
    value === "Image" ||
    value === "DetailTable" ||
    value === "PageBreak";
}
