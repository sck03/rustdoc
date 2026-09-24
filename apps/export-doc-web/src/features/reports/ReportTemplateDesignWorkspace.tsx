import type { ReportDesignerDraft } from "../report-designer/reportDesignerDraft.ts";
import { ApiReportTemplateFieldCatalogResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { ReportDesignerPage } from "../report-designer/ReportDesignerPage.tsx";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function ReportTemplateDesignWorkspace({
  reportType,
  displayName,
  content,
  fieldCatalog,
  client,
  editable,
  onDesignerDraftChange,
}: {
  reportType: ReportTypeOption;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse;
  client: ExportDocManagerApiClient;
  editable: boolean;
  onDesignerDraftChange: (draft: ReportDesignerDraft) => void;
}) {
  return (
    <div className="report-template-new-designer">
      <ReportDesignerPage
        reportType={reportType}
        displayName={displayName}
        content={content}
        fieldCatalog={fieldCatalog}
        client={client}
        editable={editable}
        onDesignerDraftChange={onDesignerDraftChange}
      />
    </div>
  );
}
