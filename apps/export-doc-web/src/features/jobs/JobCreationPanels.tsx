import type { FormEvent } from "react";
import { FileArchive, FileStack, Play, Save } from "lucide-react";
import type { ApiReportTemplateDto } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable,
  selectPdfFiles,
  selectSavePdfPath,
  selectSaveZipPath,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { PathField, PathTextAreaField } from "../../ui/PathField.tsx";
import { fileNameFromPath, readPathLines } from "./jobPresentation.ts";

export function InvoiceReportZipJobPanel({
  invoiceIds,
  invoiceCount,
  destinationPath,
  templatePath,
  withSeal,
  templates,
  templateErrorMessage,
  isTemplateLoading,
  disabled,
  canSubmit,
  onInvoiceIdsChange,
  onDestinationPathChange,
  onTemplatePathChange,
  onWithSealChange,
  onSubmit,
  onMessage,
  defaultExportDirectory,
}: {
  invoiceIds: string;
  invoiceCount: number;
  destinationPath: string;
  templatePath: string;
  withSeal: boolean;
  templates: ApiReportTemplateDto[];
  templateErrorMessage: string | null;
  isTemplateLoading: boolean;
  disabled: boolean;
  canSubmit: boolean;
  onInvoiceIdsChange: (value: string) => void;
  onDestinationPathChange: (value: string) => void;
  onTemplatePathChange: (value: string) => void;
  onWithSealChange: (value: boolean) => void;
  onSubmit: () => void;
  onMessage: (message: string | null) => void;
  defaultExportDirectory: string;
}) {
  const desktopAvailable = isDesktopBridgeAvailable();

  function handleTemplateChange(value: string) {
    onTemplatePathChange(value);
    const template = templates.find((item) => item.templatePath === value);
    if (template) onWithSealChange(template.withSealDefault ?? true);
    onMessage(null);
  }

  async function pickDestination() {
    try {
      const selected = await selectSaveZipPath("invoice-reports.zip", defaultExportDirectory);
      if (selected) {
        onDestinationPathChange(selected);
        onMessage(null);
      }
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onMessage(null);
    onSubmit();
  }

  return (
    <form className="job-tool-panel" aria-label="批量报表 ZIP 任务" onSubmit={handleSubmit}>
      {templateErrorMessage ? <InlineNotice tone="warning" title="报表模板未完整加载">{templateErrorMessage}</InlineNotice> : null}
      <div className="job-tool-grid job-report-zip-grid">
        <PathTextAreaField
          label="发票 ID"
          value={invoiceIds}
          disabled={disabled}
          onChange={(value) => { onInvoiceIdsChange(value); onMessage(null); }}
        />
        <div className="job-tool-stack">
          <div className="report-zip-options">
            <SelectField
              label="模板"
              value={templatePath}
              disabled={disabled || isTemplateLoading || templates.length === 0}
              options={templates.map((template) => ({
                value: template.templatePath,
                label: template.displayName || fileNameFromPath(template.templatePath),
              }))}
              onChange={handleTemplateChange}
            />
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={withSeal}
                disabled={disabled}
                onChange={(event) => { onWithSealChange(event.target.checked); onMessage(null); }}
              />
              <span>带章</span>
            </label>
          </div>
          {desktopAvailable ? (
            <PathField
              label="输出 ZIP"
              value={destinationPath}
              disabled={disabled}
              onChange={(value) => { onDestinationPathChange(value); onMessage(null); }}
              actions={
                <>
                  <DesktopIconButton title="选择保存位置" disabled={disabled} onClick={pickDestination}>
                    <FileArchive size={15} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(destinationPath, "打开输出位置", onMessage)}
                </>
              }
            />
          ) : <div className="field-help">ZIP 将保存到浏览器默认下载目录。</div>}
        </div>
      </div>
      <div className="job-tool-submit-row">
        <span>{invoiceCount} 张发票</span>
        <button className="solid action-button" type="submit" disabled={!canSubmit}>
          <Play size={16} aria-hidden="true" />
          <span>{desktopAvailable ? "开始" : "生成并下载"}</span>
        </button>
      </div>
    </form>
  );
}

export function PdfMergeJobPanel({
  sourcePaths,
  destinationPath,
  disabled,
  canSubmit,
  onSourcePathsChange,
  onDestinationPathChange,
  onSubmit,
  onMessage,
  defaultExportDirectory,
}: {
  sourcePaths: string;
  destinationPath: string;
  disabled: boolean;
  canSubmit: boolean;
  onSourcePathsChange: (value: string) => void;
  onDestinationPathChange: (value: string) => void;
  onSubmit: () => void;
  onMessage: (message: string | null) => void;
  defaultExportDirectory: string;
}) {
  async function pickPdfSources() {
    try {
      const selected = await selectPdfFiles();
      if (selected.length > 0) {
        onSourcePathsChange([...readPathLines(sourcePaths), ...selected]
          .filter((value, index, values) => values.indexOf(value) === index)
          .join("\n"));
      }
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  async function pickDestination() {
    try {
      const selected = await selectSavePdfPath("merged.pdf", defaultExportDirectory);
      if (selected) onDestinationPathChange(selected);
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onMessage(null);
    onSubmit();
  }

  return (
    <form className="job-tool-panel" aria-label="PDF 合并任务" onSubmit={handleSubmit}>
      <div className="job-tool-grid">
        <PathTextAreaField
          label="源 PDF"
          value={sourcePaths}
          disabled={disabled}
          onChange={onSourcePathsChange}
          actions={
            <DesktopIconButton title="选择 PDF 文件" disabled={disabled} onClick={pickPdfSources}>
              <FileStack size={15} aria-hidden="true" />
            </DesktopIconButton>
          }
        />
        <PathField
          label="输出 PDF"
          value={destinationPath}
          disabled={disabled}
          onChange={onDestinationPathChange}
          actions={
            <>
              <DesktopIconButton title="选择保存位置" disabled={disabled} onClick={pickDestination}>
                <Save size={15} aria-hidden="true" />
              </DesktopIconButton>
              {renderOpenPathAction(destinationPath, "打开输出位置", onMessage)}
            </>
          }
        />
      </div>
      <div className="job-tool-submit-row">
        <span>{readPathLines(sourcePaths).length} 个源文件</span>
        <button className="solid action-button" type="submit" disabled={!canSubmit}>
          <Play size={16} aria-hidden="true" />
          <span>开始</span>
        </button>
      </div>
    </form>
  );
}
