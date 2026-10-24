import { Eye, Save } from "lucide-react";
import { SelectField } from "../../ui/FormFields.tsx";
import { type ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";
import { type TemplatePreviewMode } from "./reportTemplateDesignerModel.ts";

export function ReportTemplatePreviewWorkspace({
  mode,
  sampleProfile,
  sampleProfiles,
  selectedSourceValue,
  sourceOptions,
  renderedHtml,
  isBusy,
  canPreview,
  canSave,
  onModeChange,
  onSampleProfileChange,
  onSourceChange,
  onPreview,
  pdf,
}: {
  mode: TemplatePreviewMode;
  sampleProfile: ReportDesignerPreviewSampleProfile;
  sampleProfiles: Array<{ value: string; label: string }>;
  selectedSourceValue: string;
  sourceOptions: Array<{ value: string; label: string }>;
  renderedHtml: string;
  isBusy: boolean;
  canPreview: boolean;
  canSave: boolean;
  onModeChange: (mode: TemplatePreviewMode) => void;
  onSampleProfileChange: (value: string) => void;
  onSourceChange: (value: string) => void;
  onPreview: () => void;
  pdf: { format: "Svg" | "PdfPage"; pageIndex: number; pageCount: number; onFormatChange: (format: "Svg" | "PdfPage") => void; onPageChange: (index: number) => void };
}) {
  return (
    <div className="report-template-preview-workspace">
      <div className="report-template-preview-toolbar">
        <SelectField label="预览方式" value={pdf.format} disabled={isBusy} options={[{ value: "Svg", label: "矢量排版" }, { value: "PdfPage", label: "实际 PDF 页图" }]} onChange={value => pdf.onFormatChange(value === "PdfPage" ? "PdfPage" : "Svg")} />
        <div className="new-report-preview-mode-tabs report-template-preview-mode-tabs" role="tablist" aria-label="模板预览数据">
          <button
            className={mode === "sample" ? "segmented-active" : ""}
            type="button"
            role="tab"
            aria-selected={mode === "sample"}
            onClick={() => onModeChange("sample")}
          >
            样例数据
          </button>
          <button
            className={mode === "savedSource" ? "segmented-active" : ""}
            type="button"
            role="tab"
            aria-selected={mode === "savedSource"}
            onClick={() => onModeChange("savedSource")}
          >
            当前单据
          </button>
        </div>
        {mode === "sample" ? (
          <SelectField
            label="样例档案"
            value={sampleProfile}
            disabled={isBusy}
            options={sampleProfiles}
            onChange={onSampleProfileChange}
          />
        ) : (
          <SelectField
            label="预览单据"
            value={selectedSourceValue}
            disabled={isBusy || sourceOptions.length === 0}
            includeEmptyOption={sourceOptions.length === 0}
            options={sourceOptions}
            onChange={onSourceChange}
          />
        )}
        <div className="report-template-preview-actions">
          <button className="command-button secondary" type="button" disabled={!canPreview} onClick={onPreview}>
            <Eye size={17} aria-hidden="true" />
            <span>{mode === "savedSource" ? "真实数据预览" : "样例预览"}</span>
          </button>
          <button className="command-button" type="submit" disabled={!canSave}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
        </div>
      </div>
      <div className="report-template-preview report-template-preview-full">
        {pdf.format === "PdfPage" && pdf.pageCount > 0 ? <div className="report-template-preview-pagination" role="group" aria-label="PDF 预览翻页">
          <button type="button" className="command-button secondary" disabled={isBusy || pdf.pageIndex <= 0} onClick={() => pdf.onPageChange(pdf.pageIndex - 1)}>上一页</button>
          <span aria-live="polite">{pdf.pageIndex + 1} / {pdf.pageCount}</span>
          <button type="button" className="command-button secondary" disabled={isBusy || pdf.pageIndex + 1 >= pdf.pageCount} onClick={() => pdf.onPageChange(pdf.pageIndex + 1)}>下一页</button>
        </div> : null}
        <iframe title="模板预览" sandbox="" srcDoc={renderedHtml} />
      </div>
    </div>
  );
}
