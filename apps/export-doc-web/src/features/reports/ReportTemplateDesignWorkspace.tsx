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
  canApplyTemplateContent,
  canSaveTemplateContent,
  hasTemplateChanges,
  canFormatSource,
  sourceDisabled,
  onApplyTemplateContent,
  onSaveTemplateContent,
  onDesignerDraftContentChange,
  onOpenSource,
  onFormatSource,
  onSourceContentChange,
}: {
  designerMode: DesignerMode;
  reportType: ReportTypeOption;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse;
  canApplyTemplateContent: boolean;
  canSaveTemplateContent: boolean;
  hasTemplateChanges: boolean;
  canFormatSource: boolean;
  sourceDisabled: boolean;
  onApplyTemplateContent: (content: string) => void;
  onSaveTemplateContent: (content: string) => void;
  onDesignerDraftContentChange: (content: string) => void;
  onOpenSource: () => void;
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
          canApplyTemplateContent={canApplyTemplateContent}
          canSaveTemplateContent={canSaveTemplateContent}
          hasTemplateChanges={hasTemplateChanges}
          onApplyTemplateContent={onApplyTemplateContent}
          onSaveTemplateContent={onSaveTemplateContent}
          onDesignerDraftContentChange={onDesignerDraftContentChange}
          onOpenSource={onOpenSource}
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
        aria-label="模板源码"
        value={content}
        disabled={sourceDisabled}
        spellCheck={false}
        onChange={(event) => onSourceContentChange(event.target.value)}
      />
    </div>
  );
}
