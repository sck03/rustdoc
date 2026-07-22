import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import type { ApiSingleWindowReferenceCatalogExcelImportPreviewResponse, ExportDocManagerApiClient, SingleWindowReferenceCatalogModel } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import {
  buildColumnMapState,
  buildExcelImportRequest,
  deduplicatePageRows,
  getRows,
  readPositiveInteger,
  setRows,
  type CatalogKey,
  type CatalogPageDefinition,
} from "./referenceCatalogModel.ts";

type Options = {
  client: ExportDocManagerApiClient;
  activeKey: CatalogKey;
  activePage: CatalogPageDefinition;
  draft: SingleWindowReferenceCatalogModel | null;
  rows: ReturnType<typeof getRows>;
  canManage: boolean;
  externalBusy: boolean;
  markDraft(next: SingleWindowReferenceCatalogModel): void;
  clearMessages(): void;
  showError(message: string): void;
  showSuccess(message: string): void;
};

export function useReferenceCatalogExcelWorkspace({
  client,
  activeKey,
  activePage,
  draft,
  rows,
  canManage,
  externalBusy,
  markDraft,
  clearMessages,
  showError,
  showSuccess,
}: Options) {
  const excelImportInputRef = useRef<HTMLInputElement | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [sheetName, setSheetName] = useState("");
  const [headerRowNumber, setHeaderRowNumber] = useState("1");
  const [dataStartRowNumber, setDataStartRowNumber] = useState("2");
  const [columnMap, setColumnMap] = useState<Record<string, string>>({});
  const [importMode, setImportMode] = useState<"append" | "replace">("append");
  const [preview, setPreview] = useState<ApiSingleWindowReferenceCatalogExcelImportPreviewResponse | null>(null);

  const previewMutation = useMutation({
    mutationFn: (selectedFile: File) => client.previewSingleWindowReferenceCatalogExcelImport({
      ...buildExcelImportRequest(activeKey, selectedFile, sheetName, headerRowNumber, dataStartRowNumber, columnMap),
      body: selectedFile,
    }),
    onSuccess: (response) => {
      setPreview(response);
      setSheetName(response.sheetName || "");
      setHeaderRowNumber(String(response.headerRowNumber || 1));
      setDataStartRowNumber(String(response.dataStartRowNumber || 2));
      setColumnMap(buildColumnMapState(response));
      clearMessages();
      showSuccess(response.message || "Excel 预览已生成。");
    },
    onError: (error) => showError(readApiError(error)),
  });

  const isBusy = externalBusy || previewMutation.isPending;
  const canPreview = canManage && Boolean(file)
    && readPositiveInteger(dataStartRowNumber, 2) > readPositiveInteger(headerRowNumber, 1)
    && !isBusy;
  const canApply = canManage && Boolean(draft) && Boolean(preview)
    && preview?.catalogKey === activePage.key && (preview?.rowCount ?? 0) > 0 && !isBusy;

  useEffect(() => {
    reset();
  }, [activeKey]);

  function reset() {
    setFile(null);
    setPreview(null);
    setSheetName("");
    setHeaderRowNumber("1");
    setDataStartRowNumber("2");
    setColumnMap({});
    setImportMode("append");
  }

  function chooseFile() {
    if (canManage && !isBusy) excelImportInputRef.current?.click();
  }
  function handleFile(event: ChangeEvent<HTMLInputElement>) {
    const selectedFile = event.currentTarget.files?.[0];
    event.currentTarget.value = "";
    if (!selectedFile || !canManage || isBusy) return;
    setFile(selectedFile);
    setPreview(null);
    setSheetName("");
    setHeaderRowNumber("1");
    setDataStartRowNumber("2");
    setColumnMap({});
    clearMessages();
    previewMutation.mutate(selectedFile);
  }
  function previewFile() {
    if (file && canPreview) {
      clearMessages();
      previewMutation.mutate(file);
    }
  }
  function updateColumn(fieldKey: string, value: string) {
    setColumnMap((current) => ({ ...current, [fieldKey]: value }));
    setPreview(null);
  }
  function applyPreview() {
    if (!draft || !preview || preview.catalogKey !== activePage.key) return;
    const importedRows = getRows(preview.catalog, activePage.key);
    if (importedRows.length === 0) {
      showError("Excel 预览没有可导入行。");
      return;
    }
    const baseRows = importMode === "append" ? rows : [];
    const nextRows = deduplicatePageRows([...baseRows, ...importedRows], activePage);
    const mergedCount = baseRows.length + importedRows.length - nextRows.length;
    markDraft(setRows(draft, activePage.key, nextRows));
    clearMessages();
    showSuccess(`已${importMode === "append" ? "追加" : "替换"} ${importedRows.length} 行到“${activePage.label}”，${mergedCount > 0 ? `并合并 ${mergedCount} 行重复项。` : "未发现重复项。"}`);
  }

  return {
    inputRef: excelImportInputRef,
    file,
    sheetName,
    headerRowNumber,
    dataStartRowNumber,
    columnMap,
    importMode,
    preview,
    isBusy,
    canPreview,
    canApply,
    chooseFile,
    handleFile,
    previewFile,
    applyPreview,
    reset,
    setSheetName(value: string) { setSheetName(value); setPreview(null); clearMessages(); },
    setHeaderRowNumber(value: string) {
      setHeaderRowNumber(value);
      const nextHeader = readPositiveInteger(value, 1);
      if (readPositiveInteger(dataStartRowNumber, 2) <= nextHeader) setDataStartRowNumber(String(nextHeader + 1));
      setPreview(null);
      clearMessages();
    },
    setDataStartRowNumber(value: string) {
      setDataStartRowNumber(value);
      const nextDataStart = readPositiveInteger(value, 2);
      if (nextDataStart <= readPositiveInteger(headerRowNumber, 1)) setHeaderRowNumber(String(Math.max(1, nextDataStart - 1)));
      setPreview(null);
      clearMessages();
    },
    setImportMode,
    updateColumn,
  };
}
