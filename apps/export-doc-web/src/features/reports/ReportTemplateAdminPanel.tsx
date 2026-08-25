import { Pencil, Plus, Trash2 } from "lucide-react";
import { TextField } from "../../ui/FormFields.tsx";

export function ReportTemplateAdminPanel({
  currentTemplateLabel,
  newTemplateFileName,
  newTemplateDisplayName,
  currentTemplateDisplayName,
  renameTemplateFileName,
  isUserTemplate,
  canManageTemplates,
  canCreate,
  canUpdateDisplayName,
  canRenameFile,
  canDelete,
  canEditDisplayName,
  canEditFileName,
  isBusy,
  onNewTemplateFileNameChange,
  onNewTemplateDisplayNameChange,
  onCurrentTemplateDisplayNameChange,
  onRenameTemplateFileNameChange,
  onCreate,
  onUpdateDisplayName,
  onRenameFile,
  onDelete,
}: {
  currentTemplateLabel: string;
  newTemplateFileName: string;
  newTemplateDisplayName: string;
  currentTemplateDisplayName: string;
  renameTemplateFileName: string;
  isUserTemplate: boolean;
  canManageTemplates: boolean;
  canCreate: boolean;
  canUpdateDisplayName: boolean;
  canRenameFile: boolean;
  canDelete: boolean;
  canEditDisplayName: boolean;
  canEditFileName: boolean;
  isBusy: boolean;
  onNewTemplateFileNameChange: (value: string) => void;
  onNewTemplateDisplayNameChange: (value: string) => void;
  onCurrentTemplateDisplayNameChange: (value: string) => void;
  onRenameTemplateFileNameChange: (value: string) => void;
  onCreate: () => void;
  onUpdateDisplayName: () => void;
  onRenameFile: () => void;
  onDelete: () => void;
}) {
  return (
    <details className="template-management-panel template-actions-panel template-admin-panel" aria-label="模板管理">
      <summary>
        <span>模板操作</span>
        <small>{currentTemplateLabel}</small>
      </summary>
      <div className="template-management-content">
        <section className="template-management-section" aria-label="新建模板">
          <div className="template-management-section-title"><strong>管理员文件模板</strong></div>
          <TextField
            label="文件名"
            value={newTemplateFileName}
            disabled={!canManageTemplates || isBusy}
            onChange={onNewTemplateFileNameChange}
          />
          <TextField
            label="显示名称"
            value={newTemplateDisplayName}
            disabled={!canManageTemplates || isBusy}
            onChange={onNewTemplateDisplayNameChange}
          />
          <button className="command-button secondary" type="button" disabled={!canCreate} onClick={onCreate}>
            <Plus size={17} aria-hidden="true" />
            <span>新建</span>
          </button>
        </section>
        <section className="template-management-section template-current-template-section" aria-label="当前模板">
          <div className="template-management-section-title"><strong>当前模板</strong></div>
          <div className="template-management-actions">
            <button
              className="icon-button danger-icon"
              type="button"
              title="删除当前模板"
              aria-label="删除当前模板"
              disabled={!canDelete}
              onClick={onDelete}
            >
              <Trash2 size={18} aria-hidden="true" />
            </button>
          </div>
          <details className="template-inline-details" open>
            <summary>
              <Pencil size={15} aria-hidden="true" />
              <span>名称与文件</span>
            </summary>
            <div className="template-inline-details-content">
              <TextField
                label="显示名称"
                value={currentTemplateDisplayName}
                disabled={!canEditDisplayName}
                onChange={onCurrentTemplateDisplayNameChange}
              />
              <button className="command-button secondary" type="button" disabled={!canUpdateDisplayName} onClick={onUpdateDisplayName}>
                <Pencil size={17} aria-hidden="true" />
                <span>保存显示名称</span>
              </button>
              {!isUserTemplate ? (
                <>
                  <TextField
                    label="文件名"
                    value={renameTemplateFileName}
                    disabled={!canEditFileName}
                    onChange={onRenameTemplateFileNameChange}
                  />
                  <button className="command-button secondary" type="button" disabled={!canRenameFile} onClick={onRenameFile}>
                    <Pencil size={17} aria-hidden="true" />
                    <span>修改文件名</span>
                  </button>
                </>
              ) : null}
            </div>
          </details>
        </section>
      </div>
    </details>
  );
}
