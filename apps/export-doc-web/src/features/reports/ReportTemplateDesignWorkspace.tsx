import { Code2 } from "lucide-react";
import { ApiReportTemplateFieldCatalogResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { ReportDesignerPage } from "../report-designer/ReportDesignerPage.tsx";
import { type DesignerMode, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function ReportTemplateDesignWorkspace({
  designerMode,
  reportType,
  displayName,
  content,
  fieldCatalog,
  client,
  canFormatSource,
  sourceDisabled,
  onDesignerDraftContentChange,
  onFormatSource,
  onSourceContentChange,
}: {
  designerMode: DesignerMode;
  reportType: ReportTypeOption;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse;
  client: ExportDocManagerApiClient;
  canFormatSource: boolean;
  sourceDisabled: boolean;
  onDesignerDraftContentChange: (content: string) => void;
  onFormatSource: () => void;
  onSourceContentChange: (content: string) => void;
}) {
  if (designerMode === "advancedHtml") {
    return (
      <div className="report-template-editor">
        <div className="report-template-source-toolbar">
          <span className="report-template-source-mode-note">复杂表格、合并单元格和精确分页由高级 HTML 保持原版式。</span>
          <button className="command-button secondary" type="button" disabled={!canFormatSource} onClick={onFormatSource}>
            <Code2 size={17} aria-hidden="true" />
            <span>格式化</span>
          </button>
        </div>
        <textarea
          aria-label="模板高级 HTML"
          value={content}
          disabled={sourceDisabled}
          spellCheck={false}
          onChange={(event) => onSourceContentChange(event.target.value)}
        />
      </div>
    );
  }

  return (
    <div className="report-template-new-designer">
      <ReportDesignerPage
        reportType={reportType}
        displayName={displayName}
        content={content}
        fieldCatalog={fieldCatalog}
        client={client}
        onDesignerDraftContentChange={onDesignerDraftContentChange}
      />
    </div>
  );
}
