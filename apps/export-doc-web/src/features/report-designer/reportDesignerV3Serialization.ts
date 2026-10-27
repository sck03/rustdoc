import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

// Share the size measured for draft export with undo retention. Keep only the
// estimate, so old snapshots do not retain an extra copy of their JSON text.
const sizes = new WeakMap<ReportDesignerV3Schema, number>();

export function serializeReportDesignerV3(schema: ReportDesignerV3Schema) {
  const content = JSON.stringify(schema);
  sizes.set(schema, content.length * 2);
  return content;
}

export function estimateReportDesignerV3Bytes(schema: ReportDesignerV3Schema) {
  return sizes.get(schema) ?? serializeReportDesignerV3(schema).length * 2;
}
