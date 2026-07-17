import { ChangeEvent, RefObject } from "react";
import { Download, FolderOpen, Upload } from "lucide-react";
import { DesktopIconButton } from "../../ui/DesktopPathActions.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { PathField } from "../../ui/PathField.tsx";
import {
  importStrategyOptions,
  normalizeImportStrategy,
  type TemplateImportStrategyOption,
} from "./reportTemplateDesignerModel.ts";

export function ReportTemplatePackagePanel({
  desktopAvailable,
  canManageTemplates,
  isBusy,
  importStrategy,
  exportPath,
  importPath,
  uploadInputRef,
  canExport,
  canExportByPath,
  canDownload,
  canImport,
  canImportByPath,
  canUpload,
  onImportStrategyChange,
  onExport,
  onExportByPath,
  onDownload,
  onImport,
  onImportByPath,
  onUpload,
  onUploadFileChange,
  onExportPathChange,
  onImportPathChange,
  onChooseExportPath,
  onChooseImportPath,
}: {
  desktopAvailable: boolean;
  canManageTemplates: boolean;
  isBusy: boolean;
  importStrategy: TemplateImportStrategyOption;
  exportPath: string;
  importPath: string;
  uploadInputRef: RefObject<HTMLInputElement>;
  canExport: boolean;
  canExportByPath: boolean;
  canDownload: boolean;
  canImport: boolean;
  canImportByPath: boolean;
  canUpload: boolean;
  onImportStrategyChange: (value: TemplateImportStrategyOption) => void;
  onExport: () => void;
  onExportByPath: () => void;
  onDownload: () => void;
  onImport: () => void;
  onImportByPath: () => void;
  onUpload: () => void;
  onUploadFileChange: (event: ChangeEvent<HTMLInputElement>) => void;
  onExportPathChange: (value: string) => void;
  onImportPathChange: (value: string) => void;
  onChooseExportPath: () => void;
  onChooseImportPath: () => void;
}) {
  return (
    <details className="template-management-panel template-package-panel" aria-label="模板包">
      <summary>
        <span>模板包</span>
        <small>导入 / 导出</small>
      </summary>
      <div className="template-management-content template-package-content">
        <section className="template-management-section template-package-command-section" aria-label="导出模板包">
          <div className="template-management-section-title"><strong>导出</strong></div>
          <button
            className="command-button secondary"
            type="button"
            disabled={desktopAvailable ? !canExport : !canDownload}
            onClick={desktopAvailable ? onExport : onDownload}
          >
            <Download size={17} aria-hidden="true" />
            <span>{desktopAvailable ? "导出包" : "下载包"}</span>
          </button>
        </section>
        <section className="template-management-section template-package-command-section" aria-label="导入模板包">
          <div className="template-management-section-title"><strong>导入</strong></div>
          <SelectField
            label="策略"
            value={importStrategy}
            disabled={!canManageTemplates || isBusy}
            options={importStrategyOptions}
            onChange={(value) => onImportStrategyChange(normalizeImportStrategy(value))}
          />
          {desktopAvailable ? (
            <button className="command-button secondary" type="button" disabled={!canImport} onClick={onImport}>
              <Upload size={17} aria-hidden="true" />
              <span>导入包</span>
            </button>
          ) : (
            <>
              <input ref={uploadInputRef} type="file" accept=".edtpl,.zip" hidden onChange={onUploadFileChange} />
              <button className="command-button secondary" type="button" disabled={!canUpload} onClick={onUpload}>
                <Upload size={17} aria-hidden="true" />
                <span>上传包</span>
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
                disabled={!canManageTemplates || isBusy}
                onChange={onExportPathChange}
                actions={
                  <DesktopIconButton title="选择导出位置" disabled={!canManageTemplates || isBusy} onClick={onChooseExportPath}>
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
                disabled={!canManageTemplates || isBusy}
                onChange={onImportPathChange}
                actions={
                  <DesktopIconButton title="选择导入包" disabled={!canManageTemplates || isBusy} onClick={onChooseImportPath}>
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
