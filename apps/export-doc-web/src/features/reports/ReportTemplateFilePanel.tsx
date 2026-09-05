import { type ChangeEvent, type Ref } from "react";
import { Download, FolderOpen, Upload } from "lucide-react";
import { DesktopIconButton } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";

export function ReportTemplateFilePanel({
  desktopAvailable,
  canExportTemplates,
  canImportTemplates,
  isBusy,
  exportPath,
  importPath,
  uploadInputRef,
  canExport,
  canImport,
  canUpload,
  canExportByPath,
  canImportByPath,
  onExport,
  onImport,
  onUpload,
  onUploadFileChange,
  onExportByPath,
  onImportByPath,
  onExportPathChange,
  onImportPathChange,
  onChooseExportPath,
  onChooseImportPath,
}: {
  desktopAvailable: boolean;
  canExportTemplates: boolean;
  canImportTemplates: boolean;
  isBusy: boolean;
  exportPath: string;
  importPath: string;
  uploadInputRef: Ref<HTMLInputElement>;
  canExport: boolean;
  canImport: boolean;
  canUpload: boolean;
  canExportByPath: boolean;
  canImportByPath: boolean;
  onExport: () => void;
  onImport: () => void;
  onUpload: () => void;
  onUploadFileChange: (event: ChangeEvent<HTMLInputElement>) => void;
  onExportByPath: () => void;
  onImportByPath: () => void;
  onExportPathChange: (value: string) => void;
  onImportPathChange: (value: string) => void;
  onChooseExportPath: () => void;
  onChooseImportPath: () => void;
}) {
  return (
    <details className="template-management-panel template-file-panel" aria-label="单个模板文件">
      <summary>
        <span>单个模板文件</span>
        <small>HTML 导入 / 导出</small>
      </summary>
      <div className="template-management-content template-package-content">
        <p className="template-management-note">适合备份或交换当前选中的一个 HTML 模板；导入会覆盖当前模板内容。</p>
        <section className="template-management-section template-package-command-section" aria-label="导出单个模板文件">
          <div className="template-management-section-title"><strong>导出</strong></div>
          <button
            className="command-button secondary"
            type="button"
            disabled={desktopAvailable ? !canExport : !canExport}
            onClick={onExport}
          >
            <Download size={17} aria-hidden="true" />
            <span>{desktopAvailable ? "导出文件" : "下载文件"}</span>
          </button>
        </section>
        <section className="template-management-section template-package-command-section" aria-label="导入单个模板文件">
          <div className="template-management-section-title"><strong>导入</strong></div>
          {desktopAvailable ? (
            <button className="command-button secondary" type="button" disabled={!canImport} onClick={onImport}>
              <Upload size={17} aria-hidden="true" />
              <span>导入文件</span>
            </button>
          ) : (
            <>
              <input ref={uploadInputRef} type="file" accept=".html,text/html" hidden onChange={onUploadFileChange} />
              <button className="command-button secondary" type="button" disabled={!canUpload} onClick={onUpload}>
                <Upload size={17} aria-hidden="true" />
                <span>上传文件</span>
              </button>
            </>
          )}
        </section>
        {desktopAvailable ? (
          <details className="template-package-advanced">
            <summary>高级路径</summary>
            <div className="template-package-advanced-content">
              <PathField
                label="导出路径"
                value={exportPath}
                disabled={!canExportTemplates || isBusy}
                onChange={onExportPathChange}
                actions={
                  <DesktopIconButton title="选择文件导出位置" disabled={!canExportTemplates || isBusy} onClick={onChooseExportPath}>
                    <FolderOpen size={15} aria-hidden="true" />
                  </DesktopIconButton>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canExportByPath} onClick={onExportByPath}>
                <Download size={17} aria-hidden="true" />
                <span>按路径导出</span>
              </button>
              <PathField
                label="导入路径"
                value={importPath}
                disabled={!canImportTemplates || isBusy}
                onChange={onImportPathChange}
                actions={
                  <DesktopIconButton title="选择导入文件" disabled={!canImportTemplates || isBusy} onClick={onChooseImportPath}>
                    <FolderOpen size={15} aria-hidden="true" />
                  </DesktopIconButton>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canImportByPath} onClick={onImportByPath}>
                <Upload size={17} aria-hidden="true" />
                <span>按路径导入</span>
              </button>
            </div>
          </details>
        ) : null}
      </div>
    </details>
  );
}
