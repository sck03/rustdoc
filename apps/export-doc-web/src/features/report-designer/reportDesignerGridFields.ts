import { updateGridCell } from "./reportDesignerGridMutations.ts";
import { validateFixedReportFieldPath } from "./reportDesignerSchemaDomains.ts";
import type { ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";
import { findV3Element, updateV3Element, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";

export type ReportDesignerGridCellTarget = { elementId: string; cellId: string };

export function bindV3GridCellField(state: ReportDesignerV3DocumentState, target: ReportDesignerGridCellTarget, fieldPath: string) {
  const located = findV3Element(state.schema, target.elementId);
  if (!located || located.element.type !== "Flow" || located.element.block.type !== "Grid") return { state, notice: "请选择普通表格中的单元格。" };
  if (located.element.locked || located.layer.locked || !located.element.visible || !located.layer.visible) return { state, notice: "当前表格已锁定或隐藏，不能修改字段。" };
  const issues: ReportDesignerSchemaIssue[] = [];
  validateFixedReportFieldPath(state.schema.reportType, fieldPath, "fieldPath", issues);
  if (!fieldPath || issues.length) return { state, notice: issues[0]?.message ?? "请选择有效的业务字段。" };
  let found = false;
  const block = updateGridCell(located.element.block, target.cellId, cell => {
    found = true;
    const contentKind = cell.contentKind === "CheckboxGroup" ? "CheckboxGroup" : "Field";
    return cell.contentKind === contentKind && cell.fieldPath === fieldPath ? cell : { ...cell, contentKind, fieldPath };
  });
  if (!found) return { state, notice: "所选单元格已不存在，请重新选择。" };
  return { state: updateV3Element(state, target.elementId, { block }), notice: null };
}
