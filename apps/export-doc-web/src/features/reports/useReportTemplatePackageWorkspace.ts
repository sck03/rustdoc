import { useRef, useState, type ChangeEvent } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { isDesktopBridgeAvailable, selectReportTemplatePackageFile, selectSaveReportTemplatePackagePath } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import type { ConfirmationRequest } from "../../ui/ConfirmationProvider.tsx";
import { useReportTemplatePackageMutations } from "./useReportTemplatePackageMutations.ts";
import { buildTemplatePackageFileName, fileNameFromPath, type ReportTypeOption, type TemplateImportStrategyOption } from "./reportTemplateDesignerModel.ts";

type Options = {
  client: ExportDocManagerApiClient; reportType: ReportTypeOption; selectedTemplatePath: string; defaultExportDirectory: string;
  requestConfirmation(request: ConfirmationRequest): Promise<boolean>;
  clearPreview(): void; showMessage(message: string | null, type: "success" | "error" | null): void;
};

export function useReportTemplatePackageWorkspace({ client, reportType, selectedTemplatePath, defaultExportDirectory, requestConfirmation, clearPreview, showMessage }: Options) {
  const uploadInputRef = useRef<HTMLInputElement | null>(null);
  const [exportPath, setExportPath] = useState(() => buildTemplatePackageFileName());
  const [importPath, setImportPath] = useState("");
  const [importStrategy, setImportStrategy] = useState<TemplateImportStrategyOption>("Merge");
  const desktopAvailable = isDesktopBridgeAvailable();
  const mutations = useReportTemplatePackageMutations({
    client, reportType, selectedTemplatePath, packageExportPath: exportPath, importStrategy,
    onExported: (response) => { setExportPath(response.packagePath); setImportPath(response.packagePath); showMessage(`模板包已导出：${response.packagePath}`, "success"); },
    onDownloaded: () => showMessage("模板包已下载。", "success"),
    onImported: (response, source) => { clearPreview(); showMessage(source === "upload" ? `模板包已上传并导入：${response.templateCount} 个模板配置。` : `模板包已导入：${response.templateCount} 个模板配置。`, "success"); },
    onError: (error) => showMessage(readApiError(error), "error"),
  });
  async function requestExportPath() { return selectSaveReportTemplatePackagePath(fileNameFromPath(exportPath.trim()) || buildTemplatePackageFileName(), defaultExportDirectory); }
  async function chooseExportPath() { try { const value = await requestExportPath(); if (value) { setExportPath(value); showMessage(null, null); } } catch (error) { showMessage(readDesktopError(error), "error"); } }
  async function chooseImportPath() { try { const value = await selectReportTemplatePackageFile(); if (value) { setImportPath(value); showMessage(null, null); } } catch (error) { showMessage(readDesktopError(error), "error"); } }
  async function exportPackage(canExport: boolean) { if (!canExport) return; if (desktopAvailable) { try { const value = await requestExportPath(); if (!value) return; setExportPath(value); mutations.exportPackageMutation.mutate(value); } catch (error) { showMessage(readDesktopError(error), "error"); } return; } if (exportPath.trim()) mutations.exportPackageMutation.mutate(exportPath.trim()); }
  function downloadPackage(canDownload: boolean) { if (canDownload) mutations.downloadPackageMutation.mutate(); }
  async function confirmImport(hasChanges: boolean, title = "导入模板包") { return !hasChanges || requestConfirmation({ title, description: "当前模板有未保存修改，确定继续吗？", details: ["未保存的编辑内容将丢失。"], confirmLabel: "继续导入" }); }
  async function importPackage(canImport: boolean, hasChanges: boolean) { if (!canImport || !await confirmImport(hasChanges)) return; if (desktopAvailable) { try { const value = await selectReportTemplatePackageFile(); if (!value) return; setImportPath(value); mutations.importPackageMutation.mutate(value); } catch (error) { showMessage(readDesktopError(error), "error"); } return; } if (importPath.trim()) mutations.importPackageMutation.mutate(importPath.trim()); }
  function exportByPath(canExport: boolean) { if (canExport && exportPath.trim()) mutations.exportPackageMutation.mutate(exportPath.trim()); }
  async function importByPath(canImport: boolean, hasChanges: boolean) { if (!canImport || !importPath.trim() || !await confirmImport(hasChanges)) return; mutations.importPackageMutation.mutate(importPath.trim()); }
  function chooseUpload(canUpload: boolean) { if (canUpload) uploadInputRef.current?.click(); }
  async function uploadFile(event: ChangeEvent<HTMLInputElement>, canUpload: boolean, hasChanges: boolean) { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; if (!file || !canUpload || !await confirmImport(hasChanges, "上传并导入模板包")) return; mutations.uploadPackageMutation.mutate(file); }
  return { uploadInputRef, exportPath, importPath, importStrategy, setExportPath, setImportPath, setImportStrategy, desktopAvailable, ...mutations, chooseExportPath, chooseImportPath, exportPackage, downloadPackage, importPackage, exportByPath, importByPath, chooseUpload, uploadFile };
}
