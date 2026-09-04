import type { ApiReportTemplateFieldCatalogResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { ReportDesignerV3Workspace } from "./ReportDesignerV3Workspace.tsx";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

/**
 * The V3 canvas is the only interactive structured designer surface. Advanced
 * HTML remains available beside it for layouts that need arbitrary table and
 * pagination control; the removed V2 format is never written or executed.
 */
export function ReportDesignerPage(props: {
  reportType: ReportDesignerReportType;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null;
  client?: ExportDocManagerApiClient;
  onDesignerDraftContentChange?: (nextContent: string) => void;
}) {
  return <ReportDesignerV3Workspace {...props} />;
}
