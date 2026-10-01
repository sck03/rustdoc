import type { FormEvent } from "react";
import type { ApiUserReportTemplateDto } from "../../api/index.ts";
import type { ConfirmationRequest } from "../../ui/ConfirmationProvider.tsx";
import type { UserReportTemplateLifecycleAction } from "./useUserReportTemplateLifecycleMutations.ts";

type Confirm = (request: ConfirmationRequest) => Promise<boolean>;

export function createReportTemplatePageActions({
  canCreateTemplate,
  createTemplate,
  canCreateBlankUserTemplate,
  createBlankUserTemplate,
  canCloneUserTemplate,
  cloneUserTemplate,
  currentUserTemplate,
  requestConfirmation,
  runUserTemplateLifecycleAction,
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
  canCreateBlankUserTemplate: boolean;
  createBlankUserTemplate: () => void;
  canCloneUserTemplate: boolean;
  cloneUserTemplate: () => void;
  currentUserTemplate: ApiUserReportTemplateDto | null;
  requestConfirmation: Confirm;
  runUserTemplateLifecycleAction: (value: UserReportTemplateLifecycleAction) => void;
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
  async function discardForCreation() {
    return !hasUnsavedChanges || await requestConfirmation({title: "保留还是放弃当前修改", description: "当前模板有未保存修改，继续会放弃这些修改。", details: ["复制使用服务器上已保存的模板内容。需要保留当前修改时，请取消并先保存。"], confirmLabel: "放弃修改并继续", tone: "danger"});
  }

  async function handleCreateTemplate() {
    if (canCreateTemplate && await discardForCreation()) createTemplate();
  }

  async function handleCreateBlankUserTemplate() {
    if (canCreateBlankUserTemplate && await discardForCreation()) createBlankUserTemplate();
  }

  async function handleCloneUserTemplate() {
    if (canCloneUserTemplate && await discardForCreation()) cloneUserTemplate();
  }

  async function handleUserTemplateLifecycleAction(action: UserReportTemplateLifecycleAction) {
    if (!currentUserTemplate) return;
    const label = action.kind === "publish"
      ? "发布"
      : action.kind === "disable"
        ? "停用"
        : action.kind === "restore"
          ? "恢复"
          : "调整共享范围";
    if (await requestConfirmation({
      title: `${label}模板`,
      description: `确定${label}“${currentUserTemplate.name}”吗？`,
      details: hasUnsavedChanges ? ["当前未保存修改将被放弃；本操作只处理服务器上已保存的版本。请先保存需要保留的修改。"] : action.kind === "publish"
        ? ["个人使用无需发布。发布后仍须主动选择共享范围；共享后原稿只读，修改请先复制或收回共享。"]
        : action.kind === "share"
          ? [action.shareScope === "Private" ? "收回共享后，其他成员不能再使用原模板，已有个人副本不受影响。" : "所选范围内有模板权限的成员可以查看、使用及复制内容。"]
        : action.kind === "restore" && currentUserTemplate.status === "Archived"
          ? ["归档模板恢复后将回到私有草稿状态。"]
          : undefined,
      confirmLabel: `确认${label}`,
    })) {
      runUserTemplateLifecycleAction(action);
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

  async function handleSetDefaultTemplate() {
    if (canSetDefault && await requestConfirmation({title: "设置全局默认模板", description: "此设置会影响团队默认输出选择，确认使用当前已保存模板吗？", details: ["个人模板无需设为全局默认，可在打印或导出时自行选择。"], confirmLabel: "设为全局默认"})) setDefaultTemplate();
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
    const isArchive = Boolean(currentUserTemplate);
    if (!await requestConfirmation({
      title: isArchive ? "归档报表模板" : "删除报表模板",
      description: isArchive ? "确定归档当前模板吗？" : "确定删除当前模板吗？",
      details: hasUnsavedChanges ? ["当前模板有未保存修改，这些修改将丢失。"] : undefined,
      confirmLabel: isArchive ? "确认归档" : "确认删除",
      tone: "danger",
    })) return;
    if (currentUserTemplate) deleteUserTemplate();
    else deleteTemplate();
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave) return;
    if (workspaceHasUnappliedDesignerChanges) {
      void saveNewDesignerContent(previewContent);
    } else if (isUserTemplate) {
      saveUserTemplate();
    } else {
      saveDefaultTemplate();
    }
  }

  return {
    handleCreateTemplate,
    handleCreateBlankUserTemplate,
    handleCloneUserTemplate,
    handleUserTemplateLifecycleAction,
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
