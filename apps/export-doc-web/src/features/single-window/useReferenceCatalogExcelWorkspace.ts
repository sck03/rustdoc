import { useLayoutEffect, useMemo, useRef, useState, type ChangeEvent } from "react";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";
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
  const scope = useMemo(() => ({ client, activeKey, canManage }), [client, activeKey, canManage]);
  const previewOperation = useTransientPreview<ApiSingleWindowReferenceCatalogExcelImportPreviewResponse, ReturnType<typeof buildExcelImportRequest> & { body: File }>(
    scope,
    (signal, request) => client.previewSingleWindowReferenceCatalogExcelImport(request, { signal }),
    error => showError(readApiError(error)),
    response => {
      setSheetName(response.sheetName || "");
      setHeaderRowNumber(String(response.headerRowNumber || 1));
      setDataStartRowNumber(String(response.dataStartRowNumber || 2));
      setColumnMap(buildColumnMapState(response));
      clearMessages();
      showSuccess(response.message || "Excel 预览已生成。");
    },
  );
  const { preview, clear } = previewOperation;

  const isBusy = externalBusy || previewOperation.isPending;
  const canPreview = canManage && Boolean(file)
    && readPositiveInteger(dataStartRowNumber, 2) > readPositiveInteger(headerRowNumber, 1)
    && !isBusy;
  const canApply = canManage && Boolean(draft) && Boolean(preview)
    && preview?.catalogKey === activePage.key && (preview?.rowCount ?? 0) > 0 && !isBusy;

  useLayoutEffect(() => {
    reset();
  }, [scope]);

  function reset() {
    setFile(null);
    clear();
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
    clear();
    setSheetName("");
    setHeaderRowNumber("1");
    setDataStartRowNumber("2");
    setColumnMap({});
    clearMessages();
    void previewOperation.generate({ ...buildExcelImportRequest(activeKey, selectedFile, "", "1", "2", {}), body: selectedFile });
  }
  function previewFile() {
    if (file && canPreview) {
      clearMessages();
      void previewOperation.generate({ ...buildExcelImportRequest(activeKey, file, sheetName, headerRowNumber, dataStartRowNumber, columnMap), body: file });
    }
  }
  function updateColumn(fieldKey: string, value: string) {
    setColumnMap((current) => ({ ...current, [fieldKey]: value }));
    clear();
  }
  function applyPreview() {
    if (!canApply || !draft || !preview || preview.catalogKey !== activePage.key) return;
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
    setSheetName(value: string) { setSheetName(value); clear(); clearMessages(); },
    setHeaderRowNumber(value: string) {
      setHeaderRowNumber(value);
      const nextHeader = readPositiveInteger(value, 1);
      if (readPositiveInteger(dataStartRowNumber, 2) <= nextHeader) setDataStartRowNumber(String(nextHeader + 1));
      clear();
      clearMessages();
    },
    setDataStartRowNumber(value: string) {
      setDataStartRowNumber(value);
      const nextDataStart = readPositiveInteger(value, 2);
      if (nextDataStart <= readPositiveInteger(headerRowNumber, 1)) setHeaderRowNumber(String(Math.max(1, nextDataStart - 1)));
      clear();
      clearMessages();
    },
    setImportMode,
    updateColumn,
  };
}

export type ReferenceCatalogExcelWorkspace = ReturnType<typeof useReferenceCatalogExcelWorkspace>;
