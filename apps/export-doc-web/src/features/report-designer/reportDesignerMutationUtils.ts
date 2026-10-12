import { createRequestKey } from "../../ui/createRequestKey.ts";

export function normalizeDesignerFieldPath(value: string) {
  const trimmed = value.trim();
  const scribanMatch = trimmed.match(/^\{\{\s*([^|}]+?)(?:\s*\|[^}]*)?\s*\}\}$/);
  return (scribanMatch?.[1] ?? trimmed).trim();
}

export function createReportBlockId(prefix: string) {
  return `block-${prefix}-${createRequestKey()}`;
}
