import { useRef, useState, type ChangeEvent } from "react";
import type { ApiReportTemplateContentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable,
  selectReportTemplateFile,
  selectSaveReportTemplateFilePath,
} from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import type { ConfirmationRequest } from "../../ui/ConfirmationProvider.tsx";
import { useReportTemplateFileMutations } from "./useReportTemplateFileMutations.ts";
import { buildTemplateFileName, fileNameFromPath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

type Options = {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  defaultExportDirectory: string;
  requestConfirmation(request: ConfirmationRequest): Promise<boolean>;
  onImported(response: ApiReportTemplateContentDto): void;
  showMessage(message: string | null, type: "success" | "error" | null): void;
};

export function useReportTemplateFileWorkspace({
  client,
  reportType,
  selectedTemplatePath,
  defaultExportDirectory,
  requestConfirmation,
  onImported,
  showMessage,
}: Options) {
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const [exportPath, setExportPath] = useState(() => buildTemplateFileName(reportType));
  const [importPath, setImportPath] = useState("");
  const desktopAvailable = isDesktopBridgeAvailable();
  const mutations = useReportTemplateFileMutations({
    client,
    reportType,
    selectedTemplatePath,
    fileExportPath: exportPath,
    onExported: (response) => {
      setExportPath(response.filePath);
      showMessage(`模板文件已导出：${response.filePath}`, "success");
    },
    onDownloaded: () => showMessage("模板文件已下载。", "success"),
    onImported: (response, source) => {
      onImported(response);
      setImportPath("");
      showMessage(source === "upload" ? "模板文件已上传并导入。" : "模板文件已导入。", "success");
    },
    onError: (error) => showMessage(readApiError(error), "error"),
  });

  async function requestExportPath() {
    return selectSaveReportTemplateFilePath(
      fileNameFromPath(exportPath.trim()) || buildTemplateFileName(reportType),
      defaultExportDirectory,
    );
  }

  async function chooseExportPath() {
    try {
      const value = await requestExportPath();
      if (value) {
        setExportPath(value);
        showMessage(null, null);
      }
    } catch (error) {
      showMessage(readDesktopError(error), "error");
    }
  }

  async function chooseImportPath() {
    try {
      const value = await selectReportTemplateFile();
      if (value) {
        setImportPath(value);
        showMessage(null, null);
      }
    } catch (error) {
      showMessage(readDesktopError(error), "error");
    }
  }

  async function confirmImport(hasChanges: boolean) {
    return !hasChanges || requestConfirmation({
      title: "导入模板文件",
      description: "当前模板有未保存修改，确定继续吗？",
      details: ["未保存的编辑内容将丢失。"],
      confirmLabel: "继续导入",
    });
  }

  async function importFile(canImport: boolean, hasChanges: boolean) {
    if (!canImport || !await confirmImport(hasChanges)) return;
    if (desktopAvailable) {
      try {
        const value = await selectReportTemplateFile();
        if (!value) return;
        setImportPath(value);
        mutations.importFileMutation.mutate(value);
      } catch (error) {
        showMessage(readDesktopError(error), "error");
      }
      return;
    }
    if (importPath.trim()) mutations.importFileMutation.mutate(importPath.trim());
  }

  function exportFile(canExport: boolean) {
    if (!canExport) return;
    if (desktopAvailable) {
      void requestExportPath().then((value) => {
        if (!value) return;
        setExportPath(value);
        mutations.exportFileMutation.mutate(value);
      }).catch((error) => showMessage(readDesktopError(error), "error"));
      return;
    }
    mutations.downloadFileMutation.mutate();
  }

  function chooseUpload(canUpload: boolean) {
    if (canUpload) uploadInputRef.current?.click();
  }

  async function uploadFile(event: ChangeEvent<HTMLInputElement>, canUpload: boolean, hasChanges: boolean) {
    const file = event.currentTarget.files?.[0];
    event.currentTarget.value = "";
    if (!file || !canUpload || !await confirmImport(hasChanges)) return;
    mutations.uploadFileMutation.mutate(file);
  }

  return {
    uploadInputRef,
    exportPath,
    importPath,
    setExportPath,
    setImportPath,
    desktopAvailable,
    ...mutations,
    chooseExportPath,
    chooseImportPath,
    exportFile,
    importFile,
    chooseUpload,
    uploadFile,
  };
}
