import { FileInput } from "../../../ui/FileInput.tsx";
import { Field } from "../../../ui/primitives/Field.tsx";
import { Download, FileSpreadsheet, Play, Save } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { DesktopIconButton, renderOpenPathAction } from "../../../ui/DesktopPathActions.tsx";
import { Link } from "react-router-dom";
import { PathField } from "../../../ui/PathField.tsx";
import { ViewJobButton } from "../../jobs/ViewJobButton.tsx";
import { InlineNotice } from "../../../ui/PageState.tsx";
import { buildBookingSheetFileName, useExcelTools } from "./useExcelTools.ts";

export function ExcelToolsPanel({
  client,
  canOperate,
  canReadInvoices,
}: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  canReadInvoices: boolean;
}) {
  const {
    desktopAvailable, message, messageType, lastCreatedJobId, setMessage, showError,
    templateDestinationPath, setTemplateDestinationPath, blankBookingDestinationPath, setBlankBookingDestinationPath,
    convertSourcePath, setConvertSourcePath, convertDestinationPath, setConvertDestinationPath, setConvertUploadFile,
    isBusy, canExportTemplate, canExportBlankBooking, canExportTemplateByPath, canExportBlankBookingByPath, canConvertBooking, canConvertBookingByPath,
    pickConvertSource, pickExcelDestination, handleExportTemplate, handleExportTemplateByPath, handleExportBlankBooking, handleExportBlankBookingByPath,
    handleConvertBooking, handleConvertBookingByPath,
  } = useExcelTools(client, canOperate);
  return (
    <section className="job-tool-panel" aria-label="Excel 模板与托单">
      <div className="tool-panel-heading">
        <div>
          <h2>Excel 模板与托单</h2>
          <span>导入模板、空白托单与托单转换</span>
        </div>
      </div>

      {message ? (
        <InlineNotice tone={messageType === "error" ? "error" : "success"} action={messageType === "success" ? <ViewJobButton jobId={lastCreatedJobId} disabled={isBusy} /> : undefined}>{message}</InlineNotice>
      ) : null}

      <fieldset className="permission-fieldset" disabled={!canOperate}>
      <div className="job-tool-grid job-excel-grid">
        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Download size={16} aria-hidden="true" />
            <strong>导入模板</strong>
          </div>
          <button className="command-button secondary" type="button" disabled={!canExportTemplate} onClick={handleExportTemplate}>
            <Download size={16} aria-hidden="true" />
            <span>导出导入模板</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="模板输出"
                value={templateDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setTemplateDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择模板保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination("导入数据模板.xlsx", setTemplateDestinationPath)}
                      >
                        <Download size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(templateDestinationPath, "打开模板输出位置", showError)}
                  </>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canExportTemplateByPath} onClick={handleExportTemplateByPath}>
                <Download size={16} aria-hidden="true" />
                <span>按路径导出</span>
              </button>
            </div>
          </details> : <span className="section-description">文件将保存到浏览器默认下载目录。</span>}
        </div>

        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Save size={16} aria-hidden="true" />
            <strong>空白托单</strong>
          </div>
          <button className="command-button secondary" type="button" disabled={!canExportBlankBooking} onClick={handleExportBlankBooking}>
            <Download size={16} aria-hidden="true" />
            <span>导出空白托单</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="托单输出"
                value={blankBookingDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setBlankBookingDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择空白托单保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination("空白托单模板.xlsx", setBlankBookingDestinationPath)}
                      >
                        <Save size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(blankBookingDestinationPath, "打开空白托单输出位置", showError)}
                  </>
                }
              />
              <button
                className="command-button secondary"
                type="button"
                disabled={!canExportBlankBookingByPath}
                onClick={handleExportBlankBookingByPath}
              >
                <Download size={16} aria-hidden="true" />
                <span>按路径导出</span>
              </button>
            </div>
          </details> : <span className="section-description">文件将保存到浏览器默认下载目录。</span>}
        </div>

        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Play size={16} aria-hidden="true" />
            <strong>Excel 转托单</strong>
          </div>
          {!desktopAvailable ? (
            <Field label="选择 Excel">
              <FileInput
                accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
                disabled={isBusy}
                onChange={(event) => {
                  const file = event.currentTarget.files?.[0] ?? null;
                  event.currentTarget.value = "";
                  setConvertUploadFile(file);
                  setMessage(null);
                }}
              />
            </Field>
          ) : null}
          <button className="command-button secondary" type="button" disabled={!canConvertBooking} onClick={handleConvertBooking}>
            <Play size={16} aria-hidden="true" />
            <span>选择 Excel 并转托单</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="转换源"
                value={convertSourcePath}
                disabled={isBusy}
                onChange={(value) => {
                  setConvertSourcePath(value);
                  setMessage(null);
                }}
                actions={
                  desktopAvailable ? (
                    <DesktopIconButton title="选择已填写模板" disabled={isBusy} onClick={pickConvertSource}>
                      <FileSpreadsheet size={15} aria-hidden="true" />
                    </DesktopIconButton>
                  ) : undefined
                }
              />
              <PathField
                label="转换输出"
                value={convertDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setConvertDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择转换保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination(buildBookingSheetFileName(convertSourcePath), setConvertDestinationPath)}
                      >
                        <Save size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(convertDestinationPath, "打开转换输出位置", showError)}
                  </>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canConvertBookingByPath} onClick={handleConvertBookingByPath}>
                <Play size={16} aria-hidden="true" />
                <span>按路径转换</span>
              </button>
            </div>
          </details> : <span className="section-description">上传文件只用于本次转换，完成后自动清理。</span>}
        </div>

        {canReadInvoices && <div className="job-tool-stack">
          <div className="job-tool-stack-title"><FileSpreadsheet size={16} aria-hidden="true" /><strong>已保存发票的托单</strong></div>
          <p className="section-description">从发票列表或单据预览中输出当前发票的托单。</p>
          <Link className="command-button secondary" to="/invoices">前往发票管理</Link>
        </div>}
      </div>
      </fieldset>
    </section>
  );
}
