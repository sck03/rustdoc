import { Eye, FileArchive, FileDown, FileSpreadsheet, Save } from "lucide-react";
import { DesktopIconButton, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { InvoiceDocumentEmailPanel } from "./InvoiceDocumentEmailPanel.tsx";
import { InvoiceDocumentPackageConfig } from "./InvoiceDocumentPackageConfig.tsx";
import { fileNameFromPath } from "./invoiceReportPreviewModel.ts";
import type { InvoiceDocumentPackageWorkspace } from "./useInvoiceDocumentPackageWorkspace.ts";
import type { InvoiceFileExportOperations } from "./useInvoiceFileExportOperations.ts";

type Props = {
  desktopAvailable: boolean;
  isBusy: boolean;
  templatesLoading: boolean;
  canGeneratePdf: boolean;
  canGenerateBookingSheet: boolean;
  canPreviewPackage: boolean;
  canSavePackageConfig: boolean;
  canEditPackageConfig: boolean;
  canGeneratePackage: boolean;
  canSendDocumentEmail: boolean;
  fileExports: InvoiceFileExportOperations;
  documentPackage: InvoiceDocumentPackageWorkspace;
  onOpenEmailSettings(): void;
  onError(message: string): void;
};

export function InvoiceReportAdvancedExportPanel({
  desktopAvailable,
  isBusy,
  templatesLoading,
  canGeneratePdf,
  canGenerateBookingSheet,
  canPreviewPackage,
  canSavePackageConfig,
  canEditPackageConfig,
  canGeneratePackage,
  canSendDocumentEmail,
  fileExports,
  documentPackage,
  onOpenEmailSettings,
  onError,
}: Props) {
  return (
    <div className="report-export-advanced-body">
      {desktopAvailable ? (
        <div className="report-pdf-controls">
          <PathField
            label="输出 PDF"
            value={fileExports.pdfDestinationPath}
            disabled={isBusy}
            onChange={fileExports.changePdfDestination}
            actions={
              <>
                <DesktopIconButton title="选择保存位置" disabled={isBusy} onClick={fileExports.pickPdfDestination}>
                  <Save size={15} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(fileExports.pdfDestinationPath, "打开输出位置", onError)}
              </>
            }
          />
          <button className="command-button secondary" type="button" disabled={!canGeneratePdf} onClick={fileExports.generatePdf}>
            <FileDown size={17} aria-hidden="true" />
            <span>生成 PDF</span>
          </button>
        </div>
      ) : null}

      {desktopAvailable ? (
        <div className="report-pdf-controls">
          <PathField
            label="托单 Excel"
            value={fileExports.bookingSheetDestinationPath}
            disabled={isBusy}
            onChange={fileExports.changeBookingSheetDestination}
            actions={
              <>
                <DesktopIconButton title="选择发票托单保存位置" disabled={isBusy} onClick={fileExports.pickBookingSheetDestination}>
                  <Save size={15} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(fileExports.bookingSheetDestinationPath, "打开发票托单输出位置", onError)}
              </>
            }
          />
          <button
            className="command-button secondary"
            type="button"
            disabled={!canGenerateBookingSheet}
            onClick={fileExports.generateBookingSheet}
          >
            <FileSpreadsheet size={17} aria-hidden="true" />
            <span>导出托单</span>
          </button>
        </div>
      ) : null}

      <div className="document-package-panel">
        <div className="document-package-heading">
          <h3>单据包</h3>
          <div className="toolbar-actions">
            <span>{documentPackage.selectedTemplates.length} 个模板</span>
            <button className="command-button secondary" type="button" disabled={!canPreviewPackage} onClick={documentPackage.previewPackage}>
              <Eye size={17} aria-hidden="true" />
              <span>预览单据包</span>
            </button>
            <button
              className="command-button secondary"
              type="button"
              title="保存当前单据包配置"
              disabled={!canSavePackageConfig}
              onClick={documentPackage.saveConfig}
            >
              <Save size={17} aria-hidden="true" />
              <span>保存配置</span>
            </button>
          </div>
        </div>

        <InvoiceDocumentPackageConfig
          canEdit={canEditPackageConfig}
          desktopAvailable={desktopAvailable}
          draft={documentPackage.configDraft}
          templateOptions={documentPackage.templateOptions}
          onAdd={documentPackage.addConfigItem}
          onChooseTemplate={documentPackage.chooseConfigTemplateFile}
          onMove={documentPackage.moveConfigItem}
          onRemove={documentPackage.removeConfigItem}
          onUpdate={documentPackage.updateConfig}
          onUpdateItem={documentPackage.updateConfigItem}
        />

        <div className="document-package-template-list" aria-label="单据包模板">
          {documentPackage.packageTemplates.length === 0 ? (
            <div className="document-package-empty">{templatesLoading ? "加载中" : "暂无模板"}</div>
          ) : (
            documentPackage.packageTemplates.map((entry) => {
              const template = entry.template;
              const state = documentPackage.templateState[template.templatePath] ?? {
                selected: entry.initiallySelected,
                withSeal: entry.withSealDefault,
              };
              return (
                <div className="document-package-template-row" key={template.templatePath}>
                  <label className="document-package-template-check">
                    <input
                      type="checkbox"
                      checked={state.selected}
                      disabled={isBusy}
                      onChange={(event) => documentPackage.changeTemplateSelected(template.templatePath, event.target.checked)}
                    />
                    <span title={template.templatePath}>{entry.displayName || fileNameFromPath(template.templatePath)}</span>
                  </label>
                  <label className="document-package-seal-check">
                    <input
                      type="checkbox"
                      checked={state.withSeal}
                      disabled={isBusy || !state.selected}
                      onChange={(event) => documentPackage.changeTemplateSeal(template.templatePath, event.target.checked)}
                    />
                    <span>带章</span>
                  </label>
                </div>
              );
            })
          )}
        </div>

        <div className="report-pdf-controls document-package-output" data-default-file-name={documentPackage.defaultFileName}>
          {desktopAvailable ? (
            <label className="toggle-field document-package-zip-check">
              <input
                type="checkbox"
                checked={documentPackage.createZip}
                disabled={isBusy}
                onChange={(event) => documentPackage.changeCreateZip(event.target.checked)}
              />
              <span>生成 ZIP</span>
            </label>
          ) : (
            <div className="field-help">浏览器将生成 ZIP，并保存到默认下载目录。</div>
          )}
          {desktopAvailable ? (
            <PathField
              label={documentPackage.createZip ? "输出 ZIP" : "输出文件夹"}
              value={documentPackage.destinationPath}
              disabled={isBusy}
              onChange={documentPackage.changeDestination}
              actions={
                <>
                  <DesktopIconButton title="选择保存位置" disabled={isBusy} onClick={documentPackage.pickDestination}>
                    <Save size={15} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(documentPackage.destinationPath, "打开输出位置", onError)}
                </>
              }
            />
          ) : null}
          <label className="toggle-field document-package-merge-check">
            <input
              type="checkbox"
              checked={documentPackage.includeMergedPdf}
              disabled={isBusy || documentPackage.selectedTemplates.length < 2}
              onChange={(event) => documentPackage.changeIncludeMergedPdf(event.target.checked)}
            />
            <span>合并 PDF</span>
          </label>
          <button className="command-button secondary" type="button" disabled={!canGeneratePackage} onClick={documentPackage.generatePackage}>
            <FileArchive size={17} aria-hidden="true" />
            <span>{desktopAvailable ? (documentPackage.createZip ? "生成 ZIP" : "导出文件夹") : "下载 ZIP"}</span>
          </button>
        </div>

        <InvoiceDocumentEmailPanel
          body={documentPackage.emailBody}
          canSend={canSendDocumentEmail}
          includeMergedPdf={documentPackage.emailIncludeMergedPdf}
          isBusy={isBusy}
          selectedTemplateCount={documentPackage.selectedTemplates.length}
          subject={documentPackage.emailSubject}
          toAddress={documentPackage.emailToAddress}
          onBodyChange={documentPackage.changeEmailBody}
          onIncludeMergedPdfChange={documentPackage.changeEmailIncludeMergedPdf}
          onOpenSettings={onOpenEmailSettings}
          onSend={documentPackage.sendEmail}
          onSubjectChange={documentPackage.changeEmailSubject}
          onToAddressChange={documentPackage.changeEmailToAddress}
        />
      </div>
    </div>
  );
}
