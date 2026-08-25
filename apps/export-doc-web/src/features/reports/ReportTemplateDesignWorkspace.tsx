import { Code2 } from "lucide-react";
import { ApiReportTemplateFieldCatalogResponse } from "../../api/index.ts";
import { ReportDesignerPage } from "../report-designer/ReportDesignerPage.tsx";
import { type DesignerMode, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function ReportTemplateDesignWorkspace({
  designerMode,
  reportType,
  displayName,
  content,
  fieldCatalog,
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
  canFormatSource: boolean;
  sourceDisabled: boolean;
  onDesignerDraftContentChange: (content: string) => void;
  onFormatSource: () => void;
  onSourceContentChange: (content: string) => void;
}) {
  if (designerMode === "new") {
    return (
      <div className="report-template-new-designer">
        <ReportDesignerPage
          reportType={reportType}
          displayName={displayName}
          content={content}
          fieldCatalog={fieldCatalog}
          onDesignerDraftContentChange={onDesignerDraftContentChange}
        />
      </div>
    );
  }

  return (
    <div className="report-template-editor">
      <div className="report-template-source-toolbar">
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
