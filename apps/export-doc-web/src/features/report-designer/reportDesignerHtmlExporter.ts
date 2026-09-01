import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";
import { exportReportDesignerV3SchemaToHtml } from "./reportDesignerV3HtmlExporter.ts";

/** V3 is the sole report-design export runtime. */
export function exportReportDesignerSchemaToHtml(schema: ReportDesignerV3Schema) {
  return schema?.version === 3 ? exportReportDesignerV3SchemaToHtml(schema) : "";
}

export { exportReportDesignerV3SchemaToHtml };
