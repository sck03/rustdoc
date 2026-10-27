import { findV3Element, updateV3Element, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { getGridCellLocations, updateGridCell } from "./reportDesignerGridMutations.ts";
import type { ReportDesignerV3Element } from "./reportDesignerV3Schema.ts";

export type ReportDesignerTextTarget = { elementId: string } & (
  | { kind: "Text" }
  | { kind: "GridCell"; cellId: string }
  | { kind: "DetailHeader"; columnId: string }
);

export type ReportDesignerCanvasTextDraft = ReportDesignerTextTarget & { text: string };

export function readDesignerCanvasText(element: ReportDesignerV3Element, target: ReportDesignerTextTarget) {
  if (target.kind === "Text" && element.type === "Text") return element.text;
  if (target.kind === "GridCell" && element.type === "Flow" && element.block.type === "Grid") {
    const cell = getGridCellLocations(element.block).find(location => location.cell.id === target.cellId)?.cell;
    return cell?.contentKind === "Text" ? cell.text : undefined;
  }
  if (target.kind === "DetailHeader" && element.type === "Flow" && element.block.type === "DetailTable") {
    return element.block.columns.find(column => column.id === target.columnId)?.title;
  }
}

export function updateDesignerCanvasText(state: ReportDesignerV3DocumentState, target: ReportDesignerTextTarget, text: string) {
  const located = findV3Element(state.schema, target.elementId);
  if (!located || located.element.locked || located.layer.locked) return state;
  const element = located.element;
  const current = readDesignerCanvasText(element, target);
  if (current === undefined || current === text) return state;
  if (target.kind === "Text" && element.type === "Text") return updateV3Element(state, element.id, { text });
  if (target.kind === "GridCell" && element.type === "Flow" && element.block.type === "Grid") {
    return updateV3Element(state, element.id, { block: updateGridCell(element.block, target.cellId, cell => ({ ...cell, text })) });
  }
  if (target.kind === "DetailHeader" && element.type === "Flow" && element.block.type === "DetailTable") {
    const columns = element.block.columns.map(column => column.id === target.columnId ? { ...column, title: text } : column);
    return updateV3Element(state, element.id, { block: { ...element.block, columns } });
  }
  return state;
}
