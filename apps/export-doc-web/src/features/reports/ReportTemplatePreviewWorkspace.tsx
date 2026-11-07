import { Eye } from "lucide-react";
import { Button } from "../../ui/Button.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
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
  onModeChange: (mode: TemplatePreviewMode) => void;
  onSampleProfileChange: (value: string) => void;
  onSourceChange: (value: string) => void;
  onPreview: () => void;
  pdf: { format: "Svg" | "PdfPage"; pageIndex: number; pageCount: number; onFormatChange: (format: "Svg" | "PdfPage") => void; onPageChange: (index: number) => void };
}) {
  return (
    <div className="report-template-preview-workspace">
      <FilterBar className="report-template-preview-toolbar" label="模板预览条件">
        <SelectField label="预览方式" value={pdf.format} disabled={isBusy} options={[{ value: "Svg", label: "矢量排版" }, { value: "PdfPage", label: "实际 PDF 页图" }]} onChange={value => pdf.onFormatChange(value === "PdfPage" ? "PdfPage" : "Svg")} />
        <div className="report-template-preview-mode-tabs">
          <SegmentedNav label="模板预览数据" value={mode} onChange={onModeChange} items={[{ key: "sample", label: "样例数据" }, { key: "savedSource", label: "当前单据" }]} />
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
          <Button disabled={!canPreview} onClick={onPreview} icon={<Eye size={18} aria-hidden="true" />}>
            {mode === "savedSource" ? "真实数据预览" : "样例预览"}
          </Button>
        </div>
      </FilterBar>
      <div className="report-template-preview report-template-preview-full">
        {pdf.format === "PdfPage" && pdf.pageCount > 0 ? <div className="report-template-preview-pagination" role="group" aria-label="PDF 预览翻页">
          <Button disabled={isBusy || pdf.pageIndex <= 0} onClick={() => pdf.onPageChange(pdf.pageIndex - 1)}>上一页</Button>
          <span aria-live="polite">{pdf.pageIndex + 1} / {pdf.pageCount}</span>
          <Button disabled={isBusy || pdf.pageIndex + 1 >= pdf.pageCount} onClick={() => pdf.onPageChange(pdf.pageIndex + 1)}>下一页</Button>
        </div> : null}
        <iframe title="模板预览" sandbox="" srcDoc={renderedHtml} />
      </div>
    </div>
  );
}
