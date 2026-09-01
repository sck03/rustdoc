import type { FormEvent } from "react";
import type { ApiUserReportTemplateDto } from "../../api/index.ts";
import type { ConfirmationRequest } from "../../ui/ConfirmationProvider.tsx";
import type { DesignerMode } from "./reportTemplateDesignerModel.ts";

type Confirm = (request: ConfirmationRequest) => Promise<boolean>;

export function createReportTemplatePageActions({
  canCreateTemplate,
  createTemplate,
  canCreateUserTemplate,
  createUserTemplate,
  currentUserTemplate,
  requestConfirmation,
  updateUserTemplateStatus,
  restoreUserTemplateVersion,
  canRenameTemplate,
  isUserTemplate,
  hasUnsavedChanges,
  renameTemplate,
  canUpdateDisplayName,
  updateDisplayName,
  canSetDefault,
  setDefaultTemplate,
  canDeleteTemplate,
  deleteUserTemplate,
  deleteTemplate,
  canSave,
  designerMode,
  workspaceHasUnappliedDesignerChanges,
  previewContent,
  saveNewDesignerContent,
  saveUserTemplate,
  saveDefaultTemplate,
  exportDefaults,
  clearFeedback,
}: {
  canCreateTemplate: boolean;
  createTemplate: () => void;
  canCreateUserTemplate: boolean;
  createUserTemplate: () => void;
  currentUserTemplate: ApiUserReportTemplateDto | null;
  requestConfirmation: Confirm;
  updateUserTemplateStatus: (value: { isActive?: boolean; shareScope?: string }) => void;
  restoreUserTemplateVersion: (version: number) => void;
  canRenameTemplate: boolean;
  isUserTemplate: boolean;
  hasUnsavedChanges: boolean;
  renameTemplate: () => void;
  canUpdateDisplayName: boolean;
  updateDisplayName: () => void;
  canSetDefault: boolean;
  setDefaultTemplate: () => void;
  canDeleteTemplate: boolean;
  deleteUserTemplate: () => void;
  deleteTemplate: () => void;
  canSave: boolean;
  designerMode: DesignerMode;
  workspaceHasUnappliedDesignerChanges: boolean;
  previewContent: string;
  saveNewDesignerContent: (content: string) => Promise<void>;
  saveUserTemplate: () => void;
  saveDefaultTemplate: () => void;
  exportDefaults: {
    onChange(path: string[], value: unknown): void;
    onSave(): void;
  };
  clearFeedback(): void;
}) {
  function handleCreateTemplate() {
    if (canCreateTemplate) createTemplate();
  }

  function handleCreateUserTemplate() {
    if (canCreateUserTemplate) createUserTemplate();
  }

  async function handleToggleUserTemplateActive() {
    if (!currentUserTemplate) return;
    const action = currentUserTemplate.isActive ? "停用" : "重新启用";
    if (await requestConfirmation({
      title: `${action}模板`,
      description: `确定${action}“${currentUserTemplate.name}”吗？`,
      confirmLabel: `确认${action}`,
    })) {
      updateUserTemplateStatus({ isActive: !currentUserTemplate.isActive });
    }
  }

  async function handleRestoreUserTemplateVersion(versionNumber: number) {
    if (await requestConfirmation({
      title: `恢复到 V${versionNumber}`,
      description: `确定恢复到 V${versionNumber} 吗？`,
      details: ["当前未保存修改将被替换。", "现有历史版本仍会保留。"],
      confirmLabel: "确认恢复",
    })) {
      restoreUserTemplateVersion(versionNumber);
    }
  }

  async function handleRenameTemplate() {
    if (!canRenameTemplate || isUserTemplate) return;
    if (hasUnsavedChanges && !await requestConfirmation({
      title: "修改模板文件名",
      description: "当前模板有未保存修改，确定继续修改文件名吗？",
      details: ["文件名修改后，默认模板和导出设置中的引用会同步更新。"],
      confirmLabel: "继续修改",
    })) return;
    renameTemplate();
  }

  function handleUpdateDisplayName() {
    if (canUpdateDisplayName) updateDisplayName();
  }

  function handleSetDefaultTemplate() {
    if (canSetDefault) setDefaultTemplate();
  }

  function handleExportSettingsChange(path: string[], value: unknown) {
    exportDefaults.onChange(path, value);
    clearFeedback();
  }

  function handleSaveExportSettings() {
    exportDefaults.onSave();
  }

  async function handleDeleteTemplate() {
    if (!canDeleteTemplate) return;
    if (!await requestConfirmation({
      title: "删除报表模板",
      description: "确定删除当前模板吗？",
      details: hasUnsavedChanges ? ["当前模板有未保存修改，这些修改将丢失。"] : undefined,
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    if (currentUserTemplate) deleteUserTemplate();
    else deleteTemplate();
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave) return;
    if (designerMode === "v3" && workspaceHasUnappliedDesignerChanges) {
      void saveNewDesignerContent(previewContent);
    } else if (isUserTemplate) {
      saveUserTemplate();
    } else {
      saveDefaultTemplate();
    }
  }

  return {
    handleCreateTemplate,
    handleCreateUserTemplate,
    handleToggleUserTemplateActive,
    handleRestoreUserTemplateVersion,
    handleRenameTemplate,
    handleUpdateDisplayName,
    handleSetDefaultTemplate,
    handleExportSettingsChange,
    handleSaveExportSettings,
    handleDeleteTemplate,
    handleSave,
  };
}
