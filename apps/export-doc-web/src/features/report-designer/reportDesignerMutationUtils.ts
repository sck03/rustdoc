export function normalizeDesignerFieldPath(value: string) {
  const trimmed = value.trim();
  const scribanMatch = trimmed.match(/^\{\{\s*([^|}]+?)(?:\s*\|[^}]*)?\s*\}\}$/);
  return (scribanMatch?.[1] ?? trimmed).trim();
}

export function createReportBlockId(prefix: string) {
  return `block-${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}
