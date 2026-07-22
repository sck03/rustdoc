import {
  ChangeEvent,
  ClipboardEvent as ReactClipboardEvent,
  KeyboardEvent as ReactKeyboardEvent,
  MouseEvent as ReactMouseEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardPaste, Download, FileSpreadsheet, Plus, RefreshCw, RotateCcw, Save, Trash2, Upload } from "lucide-react";
import {
  ApiSingleWindowReferenceCatalogExcelImportPreviewResponse,
  ExportDocManagerApiClient,
  SingleWindowReferenceCatalogModel,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import {
  buildColumnMapState,
  buildExcelImportRequest,
  CatalogCellPosition,
  CatalogColumn,
  CatalogKey,
  CatalogRow,
  catalogPages,
  cloneCatalogRow,
  deduplicatePageRows,
  getRows,
  joinAliases,
  normalizeCatalog,
  normalizePastedCellValue,
  parseAliases,
  parsePastedTableRows,
  readAliases,
  readPositiveInteger,
  readRowString,
  setRows,
  validateCatalog,
} from "./referenceCatalogModel.ts";
import { ReferenceCatalogSummary } from "./ReferenceCatalogSummary.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";

type CatalogContextMenuState = {
  x: number;
  y: number;
  cell: CatalogCellPosition | null;
};

type AliasEditorState = CatalogCellPosition & {
  value: string;
};

export function SingleWindowReferenceCatalogPage({
  client,
  canManageReferenceCatalog,
}: {
  client: ExportDocManagerApiClient;
  canManageReferenceCatalog: boolean;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const jsonImportInputRef = useRef<HTMLInputElement | null>(null);
  const excelImportInputRef = useRef<HTMLInputElement | null>(null);
  const tableFrameRef = useRef<HTMLDivElement | null>(null);
  const [activeKey, setActiveKey] = useState<CatalogKey>("countries");
  const [draft, setDraft] = useState<SingleWindowReferenceCatalogModel | null>(null);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [excelImportFile, setExcelImportFile] = useState<File | null>(null);
  const [excelSheetName, setExcelSheetName] = useState("");
  const [excelHeaderRowNumber, setExcelHeaderRowNumber] = useState("1");
  const [excelDataStartRowNumber, setExcelDataStartRowNumber] = useState("2");
  const [excelColumnMap, setExcelColumnMap] = useState<Record<string, string>>({});
  const [excelImportMode, setExcelImportMode] = useState<"append" | "replace">("append");
  const [excelPreview, setExcelPreview] = useState<ApiSingleWindowReferenceCatalogExcelImportPreviewResponse | null>(null);
  const [focusedCell, setFocusedCell] = useState<CatalogCellPosition | null>(null);
  const [contextMenu, setContextMenu] = useState<CatalogContextMenuState | null>(null);
  const [aliasEditor, setAliasEditor] = useState<AliasEditorState | null>(null);

  const catalogQuery = useQuery({
    queryKey: queryKeys.singleWindowReferenceCatalog(),
    queryFn: () => client.getSingleWindowReferenceCatalog(),
  });

  useEffect(() => {
    if (catalogQuery.data) {
      setDraft(normalizeCatalog(catalogQuery.data.catalog));
      setHasUnsavedChanges(false);
      setMessage(null);
    }
  }, [catalogQuery.data]);

  useEffect(() => {
    if (catalogQuery.isError) {
      setMessage(readApiError(catalogQuery.error));
      setSuccessMessage(null);
    }
  }, [catalogQuery.error, catalogQuery.isError]);

  const saveMutation = useMutation({
    mutationFn: (catalog: SingleWindowReferenceCatalogModel) =>
      client.updateSingleWindowReferenceCatalog({
        body: {
          catalog,
        },
      }),
    onSuccess: async (response) => {
      const nextCatalog = normalizeCatalog(response.catalog);
      setDraft(nextCatalog);
      setHasUnsavedChanges(false);
      setMessage(null);
      setSuccessMessage(response.message || "单一窗口参考词典已保存。");
      queryClient.setQueryData(queryKeys.singleWindowReferenceCatalog(), {
        catalog: nextCatalog,
        storagePolicy: response.storagePolicy,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowReferenceCatalog() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const resetMutation = useMutation({
    mutationFn: () => client.resetSingleWindowReferenceCatalog(),
    onSuccess: async (response) => {
      const nextCatalog = normalizeCatalog(response.catalog);
      setDraft(nextCatalog);
      setHasUnsavedChanges(false);
      setMessage(null);
      setSuccessMessage(response.message || "已恢复内置参考词典。");
      queryClient.setQueryData(queryKeys.singleWindowReferenceCatalog(), {
        catalog: nextCatalog,
        storagePolicy: response.storagePolicy,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowReferenceCatalog() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const importJsonMutation = useMutation({
    mutationFn: (file: File) =>
      client.importSingleWindowReferenceCatalogJson({
        body: file,
      }),
    onSuccess: async (response) => {
      const nextCatalog = normalizeCatalog(response.catalog);
      setDraft(nextCatalog);
      setHasUnsavedChanges(false);
      setMessage(null);
      setSuccessMessage(response.message || "单一窗口参考词典 JSON 已导入。");
      setExcelPreview(null);
      queryClient.setQueryData(queryKeys.singleWindowReferenceCatalog(), {
        catalog: nextCatalog,
        storagePolicy: response.storagePolicy,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowReferenceCatalog() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const previewExcelMutation = useMutation({
    mutationFn: (file: File) =>
      client.previewSingleWindowReferenceCatalogExcelImport({
        ...buildExcelImportRequest(activeKey, file, excelSheetName, excelHeaderRowNumber, excelDataStartRowNumber, excelColumnMap),
        body: file,
      }),
    onSuccess: (response) => {
      setExcelPreview(response);
      setExcelSheetName(response.sheetName || "");
      setExcelHeaderRowNumber(String(response.headerRowNumber || 1));
      setExcelDataStartRowNumber(String(response.dataStartRowNumber || 2));
      setExcelColumnMap(buildColumnMapState(response));
      setMessage(null);
      setSuccessMessage(response.message || "Excel 预览已生成。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const activePage = useMemo(
    () => catalogPages.find((page) => page.key === activeKey) ?? catalogPages[0],
    [activeKey],
  );
  const isBusy =
    catalogQuery.isFetching ||
    saveMutation.isPending ||
    resetMutation.isPending ||
    importJsonMutation.isPending ||
    previewExcelMutation.isPending;
  const rows = getRows(draft, activePage.key);
  const validationErrors = draft ? validateCatalog(draft) : [];
  const canSave = canManageReferenceCatalog && Boolean(draft) && validationErrors.length === 0 && !isBusy;
  const canPreviewExcel =
    canManageReferenceCatalog &&
    Boolean(excelImportFile) &&
    readPositiveInteger(excelDataStartRowNumber, 2) > readPositiveInteger(excelHeaderRowNumber, 1) &&
    !isBusy;
  const canApplyExcelPreview =
    canManageReferenceCatalog &&
    Boolean(draft) &&
    Boolean(excelPreview) &&
    excelPreview?.catalogKey === activePage.key &&
    (excelPreview?.rowCount ?? 0) > 0 &&
    !isBusy;
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: canManageReferenceCatalog && hasUnsavedChanges,
    message: "当前单一窗口参考词典有未保存的修改。",
  });

  useEffect(() => {
    setExcelPreview(null);
    setExcelSheetName("");
    setExcelHeaderRowNumber("1");
    setExcelDataStartRowNumber("2");
    setExcelColumnMap({});
    setFocusedCell(null);
    setContextMenu(null);
    setAliasEditor(null);
  }, [activeKey]);

  useEffect(() => {
    if (!contextMenu) {
      return;
    }

    function closeContextMenu() {
      setContextMenu(null);
    }

    function closeContextMenuOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setContextMenu(null);
      }
    }

    window.addEventListener("click", closeContextMenu);
    window.addEventListener("resize", closeContextMenu);
    window.addEventListener("keydown", closeContextMenuOnEscape);
    return () => {
      window.removeEventListener("click", closeContextMenu);
      window.removeEventListener("resize", closeContextMenu);
      window.removeEventListener("keydown", closeContextMenuOnEscape);
    };
  }, [contextMenu]);

  function markDraft(nextCatalog: SingleWindowReferenceCatalogModel) {
    setDraft(nextCatalog);
    setHasUnsavedChanges(true);
    setSuccessMessage(null);
  }

  function addRow() {
    if (!draft || !canManageReferenceCatalog) {
      return;
    }

    markDraft(setRows(draft, activePage.key, [...rows, activePage.createRow()]));
    setFocusedCell({ rowIndex: rows.length, columnIndex: 0 });
    setContextMenu(null);
  }

  function updateRow(index: number, column: CatalogColumn, value: string) {
    if (!draft || !canManageReferenceCatalog) {
      return;
    }

    const nextRows = rows.map((row, rowIndex) =>
      rowIndex === index
        ? ({
            ...row,
            [column.key]: column.kind === "aliases" ? parseAliases(value) : value,
          } as CatalogRow)
        : row,
    );
    markDraft(setRows(draft, activePage.key, nextRows));
  }

  function deleteRow(index: number) {
    if (!draft || !canManageReferenceCatalog) {
      return;
    }

    markDraft(setRows(draft, activePage.key, rows.filter((_, rowIndex) => rowIndex !== index)));
    setFocusedCell(null);
    setContextMenu(null);
  }

  async function deleteContextRow() {
    const position = contextMenu?.cell ?? focusedCell;
    if (!position || position.rowIndex < 0 || position.rowIndex >= rows.length) {
      return;
    }

    if (!await requestConfirmation({ title: "删除参考词典行", description: `确定删除“${activePage.label}”第 ${position.rowIndex + 1} 行吗？`, details: ["保存词典后修改才会正式生效。"], confirmLabel: "确认删除", tone: "danger" })) {
      return;
    }

    deleteRow(position.rowIndex);
  }

  function deduplicateRows() {
    if (!draft || !canManageReferenceCatalog) {
      return;
    }

    const nextRows = deduplicatePageRows(rows, activePage);
    markDraft(setRows(draft, activePage.key, nextRows));
    setMessage(null);
    setSuccessMessage(nextRows.length === rows.length ? "当前页没有可合并的重复项。" : "当前页重复项已合并。");
    setContextMenu(null);
  }

  function pasteCatalogText(rawText: string, startCell: CatalogCellPosition | null = focusedCell) {
    if (!draft || !canManageReferenceCatalog || isBusy) {
      return;
    }

    const pastedRows = parsePastedTableRows(rawText);
    if (pastedRows.length === 0) {
      setMessage("剪贴板里没有可粘贴的文本。");
      setSuccessMessage(null);
      return;
    }

    const startRowIndex = Math.max(0, startCell?.rowIndex ?? 0);
    const startColumnIndex = Math.max(0, startCell?.columnIndex ?? 0);
    const nextRows = rows.map((row) => cloneCatalogRow(row));
    while (nextRows.length < startRowIndex + pastedRows.length) {
      nextRows.push(activePage.createRow());
    }

    let changedCellCount = 0;
    for (const [rowOffset, pastedCells] of pastedRows.entries()) {
      if (pastedCells.every((cell) => !cell.trim())) {
        continue;
      }

      const rowIndex = startRowIndex + rowOffset;
      const nextRow = { ...nextRows[rowIndex] } as Record<string, unknown>;
      for (const [columnOffset, value] of pastedCells.entries()) {
        const columnIndex = startColumnIndex + columnOffset;
        const column = activePage.columns[columnIndex];
        if (!column) {
          continue;
        }

        nextRow[column.key] = normalizePastedCellValue(column, value);
        changedCellCount += 1;
      }

      nextRows[rowIndex] = nextRow as unknown as CatalogRow;
    }

    if (changedCellCount === 0) {
      setMessage("剪贴板里的文本为空。");
      setSuccessMessage(null);
      return;
    }

    markDraft(setRows(draft, activePage.key, nextRows));
    setFocusedCell({ rowIndex: startRowIndex, columnIndex: startColumnIndex });
    setContextMenu(null);
    setMessage(null);
    setSuccessMessage(`已批量粘贴 ${pastedRows.length} 行到“${activePage.label}”。`);
  }

  async function pasteFromSystemClipboard() {
    if (!navigator.clipboard?.readText) {
      setMessage("当前运行环境无法直接读取剪贴板，可在表格单元格内使用 Ctrl+V。");
      setSuccessMessage(null);
      return;
    }

    try {
      pasteCatalogText(await navigator.clipboard.readText(), focusedCell);
    } catch (error) {
      setMessage(`读取剪贴板失败：${readApiError(error)}`);
      setSuccessMessage(null);
    }
  }

  function handleCatalogPaste(event: ReactClipboardEvent<HTMLDivElement>) {
    const position = resolveCatalogCellPosition(event.target);
    if (!position) {
      return;
    }

    const text = event.clipboardData.getData("text/plain");
    if (!text) {
      return;
    }

    event.preventDefault();
    pasteCatalogText(text, position);
  }

  function handleCatalogKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    const position = resolveCatalogCellPosition(event.target) ?? focusedCell;
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
      event.preventDefault();
      handleSave();
      return;
    }

    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "d") {
      event.preventDefault();
      deduplicateRows();
      return;
    }

    if (
      position &&
      (event.key === "F4" || event.key === "Enter") &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey &&
      !(event.key === "Enter" && event.shiftKey) &&
      activePage.columns[position.columnIndex]?.kind === "aliases"
    ) {
      event.preventDefault();
      openAliasEditor(position);
    }
  }

  function handleCatalogContextMenu(event: ReactMouseEvent<HTMLDivElement>) {
    const position = resolveCatalogCellPosition(event.target);
    if (!position) {
      return;
    }

    event.preventDefault();
    setFocusedCell(position);
    setContextMenu({ x: event.clientX, y: event.clientY, cell: position });
  }

  function openAliasEditor(position: CatalogCellPosition | null = focusedCell) {
    if (!position || !canManageReferenceCatalog || isBusy) {
      return;
    }

    const column = activePage.columns[position.columnIndex];
    const row = rows[position.rowIndex];
    if (!row || column?.kind !== "aliases") {
      return;
    }

    setFocusedCell(position);
    setContextMenu(null);
    setAliasEditor({
      ...position,
      value: joinAliases(readAliases(row)),
    });
  }

  function applyAliasEditor() {
    if (!aliasEditor) {
      return;
    }

    const column = activePage.columns[aliasEditor.columnIndex];
    if (column?.kind === "aliases") {
      updateRow(aliasEditor.rowIndex, column, aliasEditor.value);
      setFocusedCell({ rowIndex: aliasEditor.rowIndex, columnIndex: aliasEditor.columnIndex });
    }

    setAliasEditor(null);
  }

  function handleSave() {
    if (!draft || !canManageReferenceCatalog) {
      return;
    }

    const errors = validateCatalog(draft);
    if (errors.length > 0) {
      setMessage(`词典内容校验失败：${errors.slice(0, 8).join("；")}`);
      setSuccessMessage(null);
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    saveMutation.mutate(normalizeCatalog(draft));
  }

  async function handleReset() {
    if (!canManageReferenceCatalog || isBusy) {
      return;
    }

    if (!await confirmDiscardChanges("恢复内置参考词典")) {
      return;
    }

    if (!await requestConfirmation({ title: "恢复内置参考词典", description: "确定恢复系统内置参考词典吗？", details: ["当前外置覆盖词典将被删除。"], confirmLabel: "恢复内置词典", tone: "danger" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    resetMutation.mutate();
  }

  function handleExportJson() {
    if (!draft) {
      return;
    }

    const errors = validateCatalog(draft);
    if (errors.length > 0) {
      setMessage(`词典内容校验失败：${errors.slice(0, 8).join("；")}`);
      setSuccessMessage(null);
      return;
    }

    downloadJson(normalizeCatalog(draft), "singlewindow_reference_catalogs.json");
    setMessage(null);
    setSuccessMessage("参考词典 JSON 已导出。");
  }

  function chooseJsonImportFile() {
    if (canManageReferenceCatalog && !isBusy) {
      jsonImportInputRef.current?.click();
    }
  }

  async function handleJsonImportFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.currentTarget.files?.[0];
    event.currentTarget.value = "";
    if (!file || !canManageReferenceCatalog || isBusy) {
      return;
    }

    if (!await confirmDiscardChanges("导入 JSON 参考词典")) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    importJsonMutation.mutate(file);
  }

  function chooseExcelImportFile() {
    if (canManageReferenceCatalog && !isBusy) {
      excelImportInputRef.current?.click();
    }
  }

  function handleExcelImportFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.currentTarget.files?.[0];
    event.currentTarget.value = "";
    if (!file || !canManageReferenceCatalog || isBusy) {
      return;
    }

    setExcelImportFile(file);
    setExcelPreview(null);
    setExcelSheetName("");
    setExcelHeaderRowNumber("1");
    setExcelDataStartRowNumber("2");
    setExcelColumnMap({});
    setMessage(null);
    setSuccessMessage(null);
    previewExcelMutation.mutate(file);
  }

  async function handleRefreshCatalog() {
    if (!await confirmDiscardChanges("刷新参考词典")) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    void catalogQuery.refetch();
  }

  function previewExcelImport() {
    if (excelImportFile && canPreviewExcel) {
      setMessage(null);
      setSuccessMessage(null);
      previewExcelMutation.mutate(excelImportFile);
    }
  }

  function updateExcelColumn(fieldKey: string, value: string) {
    setExcelColumnMap((current) => ({
      ...current,
      [fieldKey]: value,
    }));
    setExcelPreview(null);
    setSuccessMessage(null);
  }

  function applyExcelPreview() {
    if (!draft || !excelPreview || excelPreview.catalogKey !== activePage.key) {
      return;
    }

    const importedRows = getRows(excelPreview.catalog, activePage.key);
    if (importedRows.length === 0) {
      setMessage("Excel 预览没有可导入行。");
      setSuccessMessage(null);
      return;
    }

    const baseRows = excelImportMode === "append" ? rows : [];
    const nextRows = deduplicatePageRows([...baseRows, ...importedRows], activePage);
    const mergedCount = baseRows.length + importedRows.length - nextRows.length;
    markDraft(setRows(draft, activePage.key, nextRows));
    setMessage(null);
    setSuccessMessage(
      `已${excelImportMode === "append" ? "追加" : "替换"} ${importedRows.length} 行到“${activePage.label}”，${
        mergedCount > 0 ? `并合并 ${mergedCount} 行重复项。` : "未发现重复项。"
      }`,
    );
  }

  return (
    <section className="work-surface single-window-surface single-window-reference-catalog-surface" aria-label="单一窗口参考词典">
      <SingleWindowTabs activeKey="reference-catalog" />
      <input ref={jsonImportInputRef} hidden type="file" accept=".json,application/json" onChange={handleJsonImportFile} />
      <input
        ref={excelImportInputRef}
        hidden
        type="file"
        accept=".xlsx,.xlsm,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel.sheet.macroEnabled.12"
        onChange={handleExcelImportFile}
      />

      {!canManageReferenceCatalog ? <InlineNotice tone="info">当前账号可查看参考词典，保存和恢复需要管理员权限。</InlineNotice> : null}
      {message ? <InlineNotice tone="error" title="参考词典操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {validationErrors.length > 0 ? <InlineNotice tone="warning" title="请检查待导入数据">{validationErrors.slice(0, 4).join("；")}</InlineNotice> : null}

      <div className="toolbar single-window-reference-toolbar">
        <div className="reference-catalog-tabs" aria-label="参考词典分类">
          {catalogPages.map((page) => (
            <button
              key={page.key}
              className={page.key === activePage.key ? "reference-catalog-tab reference-catalog-tab-active" : "reference-catalog-tab"}
              type="button"
              onClick={() => setActiveKey(page.key)}
            >
              {page.label}
            </button>
          ))}
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={handleRefreshCatalog}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button secondary" type="button" disabled={!draft || isBusy} onClick={handleExportJson}>
            <Download size={17} aria-hidden="true" />
            <span>导出JSON</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canManageReferenceCatalog || isBusy} onClick={chooseJsonImportFile}>
            <Upload size={17} aria-hidden="true" />
            <span>导入JSON</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canManageReferenceCatalog || isBusy} onClick={chooseExcelImportFile}>
            <FileSpreadsheet size={17} aria-hidden="true" />
            <span>Excel导入</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canManageReferenceCatalog || isBusy} onClick={addRow}>
            <Plus size={17} aria-hidden="true" />
            <span>新增</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canManageReferenceCatalog || isBusy} onClick={() => void pasteFromSystemClipboard()}>
            <ClipboardPaste size={17} aria-hidden="true" />
            <span>批量粘贴</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canManageReferenceCatalog || rows.length === 0 || isBusy} onClick={deduplicateRows}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>去重</span>
          </button>
          <button className="command-button" type="button" disabled={!canSave} onClick={handleSave}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
          <button className="command-button danger-command" type="button" disabled={!canManageReferenceCatalog || isBusy} onClick={handleReset}>
            <RotateCcw size={17} aria-hidden="true" />
            <span>恢复内置</span>
          </button>
        </div>
      </div>

      <ReferenceCatalogSummary catalog={draft} activeKey={activePage.key} hasUnsavedChanges={hasUnsavedChanges} />

      {canManageReferenceCatalog && (excelImportFile || excelPreview) ? (
        <div className="reference-catalog-excel-panel" aria-label="Excel 导入">
          <div className="reference-catalog-excel-header">
            <div>
              <strong>{excelImportFile?.name || "Excel 导入"}</strong>
              <span>{excelPreview ? `${excelPreview.sheetName} / ${excelPreview.rowCount} 行` : activePage.label}</span>
            </div>
            <div className="toolbar-actions">
              <button className="command-button secondary" type="button" disabled={!canPreviewExcel} onClick={previewExcelImport}>
                <RefreshCw size={16} aria-hidden="true" />
                <span>预览</span>
              </button>
              <button className="command-button" type="button" disabled={!canApplyExcelPreview} onClick={applyExcelPreview}>
                <FileSpreadsheet size={16} aria-hidden="true" />
                <span>应用到草稿</span>
              </button>
            </div>
          </div>
          <div className="reference-catalog-excel-grid">
            <label>
              <span>工作表</span>
              <select
                value={excelSheetName}
                disabled={isBusy || !excelPreview?.sheetNames?.length}
                onChange={(event) => {
                  setExcelSheetName(event.target.value);
                  setExcelPreview(null);
                  setSuccessMessage(null);
                }}
              >
                {excelPreview?.sheetNames?.length ? (
                  excelPreview.sheetNames.map((sheetName) => (
                    <option key={sheetName} value={sheetName}>
                      {sheetName}
                    </option>
                  ))
                ) : (
                  <option value="">自动</option>
                )}
              </select>
            </label>
            <label>
              <span>表头行</span>
              <input
                type="number"
                min={1}
                value={excelHeaderRowNumber}
                disabled={isBusy}
                onChange={(event) => {
                  const nextHeaderRow = event.target.value;
                  setExcelHeaderRowNumber(nextHeaderRow);
                  const nextHeader = readPositiveInteger(nextHeaderRow, 1);
                  const nextDataStart = readPositiveInteger(excelDataStartRowNumber, 2);
                  if (nextDataStart <= nextHeader) {
                    setExcelDataStartRowNumber(String(nextHeader + 1));
                  }
                  setExcelPreview(null);
                  setSuccessMessage(null);
                }}
              />
            </label>
            <label>
              <span>数据起始行</span>
              <input
                type="number"
                min={1}
                value={excelDataStartRowNumber}
                disabled={isBusy}
                onChange={(event) => {
                  const nextDataStartRow = event.target.value;
                  setExcelDataStartRowNumber(nextDataStartRow);
                  const nextDataStart = readPositiveInteger(nextDataStartRow, 2);
                  const nextHeader = readPositiveInteger(excelHeaderRowNumber, 1);
                  if (nextDataStart <= nextHeader) {
                    setExcelHeaderRowNumber(String(Math.max(1, nextDataStart - 1)));
                  }
                  setExcelPreview(null);
                  setSuccessMessage(null);
                }}
              />
            </label>
            <label>
              <span>导入方式</span>
              <select value={excelImportMode} disabled={isBusy} onChange={(event) => setExcelImportMode(event.target.value === "replace" ? "replace" : "append")}>
                <option value="append">追加并去重</option>
                <option value="replace">替换当前页</option>
              </select>
            </label>
            {activePage.columns.map((column) => (
              <label key={column.key}>
                <span>{column.label}列号</span>
                <input
                  type="number"
                  min={0}
                  value={excelColumnMap[column.key] ?? ""}
                  disabled={isBusy}
                  onChange={(event) => updateExcelColumn(column.key, event.target.value)}
                />
              </label>
            ))}
          </div>
        </div>
      ) : null}

      <div
        className="table-frame reference-catalog-table-frame"
        ref={tableFrameRef}
        role="region"
        aria-label="单一窗口参考目录编辑表"
        aria-busy={isBusy}
        tabIndex={0}
        onContextMenu={handleCatalogContextMenu}
        onKeyDown={handleCatalogKeyDown}
        onPaste={handleCatalogPaste}
      >
        <table className="reference-catalog-table">
          <thead>
            <tr>
              {activePage.columns.map((column) => (
                <th key={column.key}>{column.label}</th>
              ))}
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? (
              rows.map((row, index) => (
                <tr key={`${activePage.key}-${index}`}>
                  {activePage.columns.map((column, columnIndex) => (
                    <td key={column.key} className={column.kind === "aliases" ? "reference-catalog-alias-cell" : undefined}>
                      {column.kind === "aliases" ? (
                        <textarea
                          data-catalog-row={index}
                          data-catalog-column={columnIndex}
                          value={joinAliases(readAliases(row))}
                          disabled={!canManageReferenceCatalog || isBusy}
                          aria-label={`${activePage.label} 第 ${index + 1} 行 ${column.label}`}
                          onFocus={() => setFocusedCell({ rowIndex: index, columnIndex })}
                          onChange={(event) => updateRow(index, column, event.target.value)}
                        />
                      ) : (
                        <input
                          data-catalog-row={index}
                          data-catalog-column={columnIndex}
                          value={readRowString(row, column.key)}
                          disabled={!canManageReferenceCatalog || isBusy}
                          aria-label={`${activePage.label} 第 ${index + 1} 行 ${column.label}`}
                          onFocus={() => setFocusedCell({ rowIndex: index, columnIndex })}
                          onChange={(event) => updateRow(index, column, event.target.value)}
                        />
                      )}
                    </td>
                  ))}
                  <td>
                    <button
                      className="icon-button danger-icon"
                      type="button"
                      title="删除" aria-label="删除"
                      disabled={!canManageReferenceCatalog || isBusy}
                      onClick={() => deleteRow(index)}
                    >
                      <Trash2 size={17} aria-hidden="true" />
                    </button>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={activePage.columns.length + 1}>
                  {isBusy ? "加载中" : "暂无词典行"}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {contextMenu ? (
        <div
          className="reference-catalog-context-menu"
          role="menu"
          style={{ left: contextMenu.x, top: contextMenu.y }}
          onClick={(event) => event.stopPropagation()}
          onContextMenu={(event) => event.preventDefault()}
          onMouseDown={(event) => event.stopPropagation()}
        >
          <button type="button" role="menuitem" disabled={!canManageReferenceCatalog || isBusy} onClick={addRow}>
            新增一行
          </button>
          <button type="button" role="menuitem" disabled={!canManageReferenceCatalog || isBusy || !contextMenu.cell} onClick={deleteContextRow}>
            删除当前行
          </button>
          <button type="button" role="menuitem" disabled={!canManageReferenceCatalog || isBusy} onClick={() => void pasteFromSystemClipboard()}>
            批量粘贴
          </button>
          <button type="button" role="menuitem" disabled={!canManageReferenceCatalog || isBusy || rows.length === 0} onClick={deduplicateRows}>
            批量去重
          </button>
          <button
            type="button"
            role="menuitem"
            disabled={!canManageReferenceCatalog || isBusy || activePage.columns[contextMenu.cell?.columnIndex ?? -1]?.kind !== "aliases"}
            onClick={() => openAliasEditor(contextMenu.cell)}
          >
            编辑别名...
          </button>
        </div>
      ) : null}

      {aliasEditor ? (
        <div className="single-window-lock-backdrop">
          <div className="single-window-lock-dialog reference-catalog-alias-dialog" role="dialog" aria-modal="true" aria-labelledby="reference-catalog-alias-title">
            <div className="single-window-lock-header">
              <div className="single-window-lock-title">
                <h2 id="reference-catalog-alias-title">编辑别名</h2>
                <span>{activePage.label}</span>
              </div>
            </div>
            <div className="single-window-lock-toolbar">
              <span>第 {aliasEditor.rowIndex + 1} 行</span>
            </div>
            <textarea
              autoFocus
              value={aliasEditor.value}
              onChange={(event) => setAliasEditor((current) => (current ? { ...current, value: event.target.value } : current))}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  setAliasEditor(null);
                }

                if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
                  event.preventDefault();
                  applyAliasEditor();
                }
              }}
            />
            <div className="single-window-lock-footer">
              <button className="command-button secondary" type="button" onClick={() => setAliasEditor(null)}>
                取消
              </button>
              <button className="command-button" type="button" onClick={applyAliasEditor}>
                应用
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function resolveCatalogCellPosition(target: EventTarget | null): CatalogCellPosition | null {
  if (!(target instanceof Element)) {
    return null;
  }

  const element = target.closest<HTMLElement>("[data-catalog-row][data-catalog-column]");
  if (!element) {
    return null;
  }

  const rowIndex = Number(element.dataset.catalogRow);
  const columnIndex = Number(element.dataset.catalogColumn);
  return Number.isInteger(rowIndex) && rowIndex >= 0 && Number.isInteger(columnIndex) && columnIndex >= 0
    ? { rowIndex, columnIndex }
    : null;
}

function downloadJson(catalog: SingleWindowReferenceCatalogModel, fileName: string) {
  const blob = new Blob([JSON.stringify(catalog, null, 2)], { type: "application/json;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
