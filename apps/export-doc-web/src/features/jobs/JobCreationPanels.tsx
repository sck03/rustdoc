import type { FormEvent } from "react";
import { FileArchive, FileStack, Play, Save } from "lucide-react";
import type { ApiInvoiceListItemDto, ApiReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable,
  selectPdfFiles,
  selectSavePdfPath,
  selectSaveZipPath,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { PathField, PathTextAreaField } from "../../ui/PathField.tsx";
import { readPathLines } from "./jobPresentation.ts";
import { InvoiceReportSelection } from "./InvoiceReportSelection.tsx";
import { ReportTemplateChecklist } from "./ReportTemplateChecklist.tsx";
import { invoiceReportBatchIssue } from "./useInvoiceReportBatchOptions.ts";

export function InvoiceReportZipJobPanel({
  client,
  invoices,
  destinationPath,
  templatePaths,
  withSeal,
  templates,
  templateErrorMessage,
  isTemplateLoading,
  disabled,
  canSubmit,
  onInvoicesChange,
  onDestinationPathChange,
  onTemplatePathsChange,
  onWithSealChange,
  onSubmit,
  onMessage,
  defaultExportDirectory,
}: {
  client: ExportDocManagerApiClient;
  invoices: ApiInvoiceListItemDto[];
  destinationPath: string;
  templatePaths: string[];
  withSeal: boolean;
  templates: ApiReportTemplateDto[];
  templateErrorMessage: string | null;
  isTemplateLoading: boolean;
  disabled: boolean;
  canSubmit: boolean;
  onInvoicesChange: (value: ApiInvoiceListItemDto[]) => void;
  onDestinationPathChange: (value: string) => void;
  onTemplatePathsChange: (value: string[]) => void;
  onWithSealChange: (value: boolean) => void;
  onSubmit: () => void;
  onMessage: (message: string | null) => void;
  defaultExportDirectory: string;
}) {
  const desktopAvailable = isDesktopBridgeAvailable();

  const issue = invoiceReportBatchIssue(invoices.length, templatePaths, templates);

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
    if (canSubmit && !issue) onSubmit();
  }

  return (
    <form className="job-tool-panel" aria-label="批量报表 ZIP 任务" onSubmit={handleSubmit}>
      {templateErrorMessage ? <InlineNotice tone="warning" title="报表模板未完整加载">{templateErrorMessage}</InlineNotice> : null}
      <div className="job-tool-grid job-report-zip-grid">
        <InvoiceReportSelection client={client} selected={invoices} disabled={disabled}
          onChange={(value) => { onInvoicesChange(value); onMessage(null); }} />
        <div className="job-tool-stack">
          <div className="report-zip-options">
            <ReportTemplateChecklist templates={templates} selected={templatePaths} disabled={disabled || isTemplateLoading} onChange={paths => { onTemplatePathsChange(paths); onMessage(null); }} />
            <label className="checkbox-field">
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
          ) : <div className="field-help">生成后由浏览器下载 ZIP 文件。</div>}
        </div>
      </div>
      <div className="job-tool-submit-row">
        <span>{invoices.length} 张发票 × {templatePaths.length} 种模板 = {invoices.length * templatePaths.length} 份 PDF</span>
        <button className="solid action-button" type="submit" disabled={!canSubmit || Boolean(issue)}>
          <Play size={16} aria-hidden="true" />
          <span>{desktopAvailable ? "开始" : "生成并下载"}</span>
        </button>
      </div>
      {issue && invoices.length > 0 && !isTemplateLoading && <InlineNotice tone="warning">{issue}</InlineNotice>}
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
