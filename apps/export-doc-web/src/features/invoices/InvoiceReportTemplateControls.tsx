import { FileDown, FileSpreadsheet, LayoutTemplate } from "lucide-react";
import type { ApiReportTemplateDto } from "../../api/index.ts";
import { fileNameFromPath } from "./invoiceReportPreviewModel.ts";
type Props={canConfigureOutput:boolean;canOpenTemplateManagement:boolean;canQuickGenerateBookingSheet:boolean;canQuickGeneratePdf:boolean;desktopAvailable:boolean;hasSavedInvoice:boolean;isBusy:boolean;selectedTemplatePath:string;templates:ApiReportTemplateDto[];withSeal:boolean;onExportBookingSheet():void;onExportPdf():void;onOpenTemplateManagement():void;onTemplateChange(value:string):void;onWithSealChange(value:boolean):void};
export function InvoiceReportTemplateControls(p:Props){const {canConfigureOutput,canOpenTemplateManagement,canQuickGenerateBookingSheet,canQuickGeneratePdf,desktopAvailable,hasSavedInvoice,isBusy,selectedTemplatePath,templates,withSeal}=p;return (
      <div className="report-preview-controls">
        <div className="report-template-selector">
          <label className="report-template-inline-field">
            <span>模板</span>
            <select
              value={selectedTemplatePath}
              disabled={!canConfigureOutput || isBusy || templates.length === 0}
              onChange={(event) => p.onTemplateChange(event.target.value)}
            >
              <option value="">未选择</option>
              {templates.map((template) => (
                <option key={template.templatePath} value={template.templatePath}>
                  {template.displayName || fileNameFromPath(template.templatePath)}
                </option>
              ))}
            </select>
          </label>
          <label className="toggle-field report-template-seal-toggle">
            <input
              type="checkbox"
              checked={withSeal}
              disabled={!canConfigureOutput || isBusy}
              onChange={(event) => p.onWithSealChange(event.target.checked)}
            />
            <span>带章</span>
          </label>
        </div>
        <div className="report-template-tools">
          <div className="report-template-action-group report-template-action-group-primary">
            <button
              className="command-button secondary"
              type="button"
              title={hasSavedInvoice ? (desktopAvailable ? "选择保存位置并生成 PDF" : "当前平台请使用高级导出") : "请先保存发票"}
              disabled={!hasSavedInvoice || !canQuickGeneratePdf}
              onClick={p.onExportPdf}
            >
              <FileDown size={17} aria-hidden="true" />
              <span>导出 PDF</span>
            </button>
            <button
              className="command-button secondary"
              type="button"
              title={hasSavedInvoice ? (desktopAvailable ? "选择保存位置并导出托单 Excel" : "当前平台请使用高级导出") : "请先保存发票"}
              disabled={!hasSavedInvoice || !canQuickGenerateBookingSheet}
              onClick={p.onExportBookingSheet}
            >
              <FileSpreadsheet size={17} aria-hidden="true" />
              <span>导出托单</span>
            </button>
          </div>
          <div className="report-template-action-group">
            <button
              className="command-button secondary"
              type="button"
              title="打开报表模板管理"
              disabled={!canOpenTemplateManagement || isBusy}
              onClick={p.onOpenTemplateManagement}
            >
              <LayoutTemplate size={17} aria-hidden="true" />
              <span>报表模板管理</span>
            </button>
          </div>
        </div>
      </div>


);}
