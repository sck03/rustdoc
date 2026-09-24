import type { ReportGridDiagonalHeader } from "./reportDesignerSchema.ts";
import {
  isRecord,
  readString,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

export function normalizeGridDiagonalHeader(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportGridDiagonalHeader | undefined {
  if (value === undefined || value === null) return undefined;
  if (!isRecord(value)) {
    issues.push({ severity: "warning", path, message: "斜线表头设置无效,已忽略。" });
    return undefined;
  }
  return {
    direction: value.direction === "Down" ? "Down" : "Up",
    upperLeftText: readString(value.upperLeftText, "", `${path}.upperLeftText`, issues),
    lowerRightText: readString(value.lowerRightText, "", `${path}.lowerRightText`, issues),
  };
}
