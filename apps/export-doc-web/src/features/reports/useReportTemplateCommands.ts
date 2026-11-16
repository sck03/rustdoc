import { useState, type Dispatch, type SetStateAction } from "react";
import type { ApiReportTemplateContentDto, ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useReportTemplateDocument } from "./useReportTemplateDocument.ts";
import { useReportTemplateSaveMutations } from "./useReportTemplateSaveMutations.ts";
import { useUserReportTemplateLifecycleMutations } from "./useUserReportTemplateLifecycleMutations.ts";
import { useDefaultReportTemplateLifecycleMutations } from "./useDefaultReportTemplateLifecycleMutations.ts";
import { useReportTemplateReload } from "./useReportTemplateReload.ts";
import { buildNewTemplateFileName, buildUserTemplateKey, fileNameFromPath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import { reportTemplateShareScopeLabel } from "./ReportTemplateUserPanel.tsx";

/** Owns management inputs and the single accepted-document transition after every command. */
export function useReportTemplateCommands({ client, reportType, selectedTemplatePath, selectedUserTemplateId,
  setSelectedTemplatePath, setSelectedUserTemplateId, setSavedTemplatePath, currentUserTemplate, editor, clearPreview, clearSelectionFeedback, showFeedback }: {
  client: ExportDocManagerApiClient; reportType: ReportTypeOption; selectedTemplatePath: string; selectedUserTemplateId: number;
  setSelectedTemplatePath: Dispatch<SetStateAction<string>>; setSelectedUserTemplateId: Dispatch<SetStateAction<number>>;
  setSavedTemplatePath: Dispatch<SetStateAction<string | null>>; currentUserTemplate: ApiUserReportTemplateDto | null;
  editor: ReturnType<typeof useReportTemplateDocument>; clearPreview: () => void; clearSelectionFeedback: () => void;
  showFeedback: (message: string, type: "success" | "error") => void;
}) {
  const { document, loadFile, loadUser, acceptName, clear: clearDocument } = editor;
  const { content, revision: expectedRevision, userVersion: expectedUserVersion, name: currentTemplateDisplayName } = document;
  const emptyInputs = { reportType, fileName: buildNewTemplateFileName(reportType), displayName: "", userName: "" };
  const [inputState, setInputs] = useState(emptyInputs);
  const inputs = inputState.reportType === reportType ? inputState : emptyInputs;
  if (inputState.reportType !== reportType) setInputs(emptyInputs);
  const { fileName: newTemplateFileName, displayName: newTemplateDisplayName, userName: newUserTemplateName } = inputs;
  const setNewTemplateFileName = (fileName: string) => setInputs({ ...inputs, fileName });
  const setNewTemplateDisplayName = (displayName: string) => setInputs({ ...inputs, displayName });
  const setNewUserTemplateName = (userName: string) => setInputs({ ...inputs, userName });
  const [rename, setRename] = useState({ path: "", value: "" });
  const renameTemplateFileName = rename.path === document.path ? rename.value : fileNameFromPath(document.path);
  if (rename.path !== document.path) setRename({ path: document.path, value: renameTemplateFileName });
  const setRenameTemplateFileName = (value: string) => setRename({ path: document.path, value });
  function acceptFile(template: ApiReportTemplateContentDto) {
    setSelectedUserTemplateId(0); setSelectedTemplatePath(template.templatePath); loadFile(template, true);
    setRename({ path: template.templatePath, value: fileNameFromPath(template.templatePath) });
  }
  function acceptUser(template: ApiUserReportTemplateDto) {
    setSelectedUserTemplateId(template.id); setSelectedTemplatePath(buildUserTemplateKey(template.id)); loadUser(template, true);
  }
  function clearSelection() {
    setSelectedUserTemplateId(0); setSelectedTemplatePath(""); clearDocument(); setRename({ path: "", value: "" });
  }
  const { saveDefaultTemplateMutation: saveMutation, saveUserTemplateMutation } = useReportTemplateSaveMutations({
    client,
    reportType,
    selectedTemplatePath,
    expectedRevision,
    expectedUserVersion,
    currentUserTemplate,
    content,
    userTemplateName: currentTemplateDisplayName,
    onDefaultTemplateSaved: (saved) => {
      acceptFile(saved);
      setSavedTemplatePath(saved.templatePath);
      showFeedback("模板已保存。", "success");
    },
    onUserTemplateSaved: (saved) => {
      acceptUser(saved);
      showFeedback("私人模板已保存，可直接用于自己的打印和导出，无需发布。", "success");
    },
    onError: (error) => showFeedback(readApiError(error), "error"),
  });

  const {
    createBlankUserTemplateMutation,
    cloneUserTemplateMutation,
    archiveUserTemplateMutation,
    restoreUserTemplateVersionMutation,
    updateUserTemplateStatusMutation,
  } = useUserReportTemplateLifecycleMutations({
    client,
    reportType,
    selectedTemplatePath,
    selectedUserTemplateId,
    currentUserTemplate,
    newTemplateName: newUserTemplateName,
    onCreated: (created) => {
      acceptUser(created);
      setNewUserTemplateName("");
      showFeedback("私人模板已创建，可继续设计，保存后直接个人使用。", "success");
    },
    onArchived: () => {
      clearSelection();
      showFeedback("模板已归档，可从归档列表恢复为草稿。", "success");
    },
    onRestored: (saved) => {
      acceptUser(saved);
      showFeedback(`已恢复到版本 ${saved.versionNumber}，请检查后继续编辑。`, "success");
    },
    onStatusUpdated: (saved, action) => {
      acceptUser(saved);
      const nextMessage = action.kind === "publish"
        ? "模板已发布，当前仍按原共享范围可见；可另行选择共享范围。"
        : action.kind === "disable"
          ? "模板已停用，不再用于正式输出。"
          : action.kind === "restore"
            ? saved.status === "Draft" ? "归档模板已恢复为私有草稿。" : "模板已恢复发布。"
            : `共享范围已更新：${reportTemplateShareScopeLabel(saved.shareScope)}`;
      showFeedback(nextMessage, "success");
    },
    onError: (error) => showFeedback(readApiError(error), "error"),
  });

  const {
    createTemplateMutation,
    renameTemplateMutation,
    updateDisplayNameMutation,
    setDefaultTemplateMutation,
    deleteTemplateMutation,
  } =
    useDefaultReportTemplateLifecycleMutations({
      client,
      reportType,
      selectedTemplatePath,
      expectedRevision,
      newTemplateFileName,
      newTemplateDisplayName,
      currentTemplateDisplayName,
      renameTemplateFileName,
      onCreated: (created) => {
        acceptFile(created);
        setInputs(emptyInputs);
        clearPreview();
        showFeedback("模板已新建。", "success");
      },
      onRenamed: (renamed) => {
        acceptFile(renamed);
        clearPreview();
        showFeedback("模板文件名已更新。", "success");
      },
      onDisplayNameUpdated: (updated) => {
        acceptName(updated.displayName, updated.revision);
        showFeedback("模板显示名称已更新，文件名保持不变。", "success");
      },
      onDefaultSet: (nextMessage) => showFeedback(nextMessage, "success"),
      onDeleted: () => {
        clearSelection();
        clearPreview();
        showFeedback("模板已删除。", "success");
      },
      onError: (error) => showFeedback(readApiError(error), "error"),
    });

  const reloadTemplate = useReportTemplateReload({
    client, reportType, selectedTemplatePath, selectedUserTemplateId,
    onLoaded: result => {
      if (result.kind === "user") acceptUser(result.template);
      else acceptFile(result.template);
      clearSelectionFeedback();
    },
    onError: error => showFeedback(readApiError(error), "error"),
  });

  return { newTemplateFileName, newTemplateDisplayName, newUserTemplateName, renameTemplateFileName,
    setNewTemplateFileName, setNewTemplateDisplayName, setNewUserTemplateName, setRenameTemplateFileName,
    saveMutation, saveUserTemplateMutation, createBlankUserTemplateMutation, cloneUserTemplateMutation, archiveUserTemplateMutation, restoreUserTemplateVersionMutation, updateUserTemplateStatusMutation, createTemplateMutation, renameTemplateMutation, updateDisplayNameMutation, setDefaultTemplateMutation, deleteTemplateMutation, reloadTemplate,
    isPending: [saveMutation, saveUserTemplateMutation, createBlankUserTemplateMutation, cloneUserTemplateMutation, archiveUserTemplateMutation, restoreUserTemplateVersionMutation, updateUserTemplateStatusMutation, createTemplateMutation, renameTemplateMutation, updateDisplayNameMutation, setDefaultTemplateMutation, deleteTemplateMutation, reloadTemplate].some(command => command.isPending),
  };
}
