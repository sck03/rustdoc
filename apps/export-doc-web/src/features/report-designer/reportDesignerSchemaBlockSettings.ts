import type { ReportBlockOutputSettings } from "./reportDesignerSchema.ts";
import {
  createIssue,
  isRecord,
  readBoolean,
  readOptionalString,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

export function normalizeBlockOutputSettings(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportBlockOutputSettings {
  if (value === undefined || value === null) {
    return {
      enabled: true,
    };
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "组件输出设置无效，已使用默认设置。"));
    return {
      enabled: true,
    };
  }

  const note = readOptionalString(value.note, `${path}.note`, issues);
  return {
    enabled: readBoolean(value.enabled, true, `${path}.enabled`, issues),
    note: note?.slice(0, 500),
  };
}
