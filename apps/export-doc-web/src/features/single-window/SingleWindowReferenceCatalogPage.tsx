import "../../styles/routes/single-window.css";
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
import {
  ExportDocManagerApiClient,
  SingleWindowReferenceCatalogModel,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useModalDialog } from "../../ui/useModalDialog.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { getClipboardPasteInstruction, readClipboardText } from "../../ui/clipboard.ts";
import {
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
  readRowString,
  setRows,
  validateCatalog,
} from "./referenceCatalogModel.ts";
import { ReferenceCatalogSummary } from "./ReferenceCatalogSummary.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { useReferenceCatalogExcelWorkspace } from "./useReferenceCatalogExcelWorkspace.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import {
  ReferenceCatalogAliasDialog,
  ReferenceCatalogContextMenu,
  ReferenceCatalogExcelPanel,
  ReferenceCatalogTable,
  ReferenceCatalogToolbar,
  type AliasEditorState,
  type CatalogContextMenuState,
} from "./ReferenceCatalogWorkspaceView.tsx";

export function SingleWindowReferenceCatalogPage({
  client,
}: {
  client: ExportDocManagerApiClient;
}) {
  const declarationPermission = useModulePermission("document.declaration-dictionary");
  // Operate grants edit/import access; only Manage may reset the shared dictionary.
  const canManageReferenceCatalog = declarationPermission.canOperate;
  const canResetReferenceCatalog = declarationPermission.canManage;
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const jsonImportInputRef = useRef<HTMLInputElement | null>(null);
  const tableFrameRef = useRef<HTMLDivElement | null>(null);
  const [activeKey, setActiveKey] = useState<CatalogKey>("countries");
  const [draft, setDraft] = useState<SingleWindowReferenceCatalogModel | null>(null);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [focusedCell, setFocusedCell] = useState<CatalogCellPosition | null>(null);
  const [contextMenu, setContextMenu] = useState<CatalogContextMenuState | null>(null);
  const [aliasEditor, setAliasEditor] = useState<AliasEditorState | null>(null);
  const aliasEditorInputRef = useRef<HTMLTextAreaElement | null>(null);
  const aliasDialogRef = useModalDialog<HTMLDivElement>(() => setAliasEditor(null), {
    active: Boolean(aliasEditor),
    initialFocusRef: aliasEditorInputRef,
  });

  const catalogQuery = useQuery({
    queryKey: queryKeys.singleWindowReferenceCatalog(),
    queryFn: ({ signal }) => client.getSingleWindowReferenceCatalog({ signal }),
  });

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
      setSuccessMessage(response.message || "单一窗口参考词典已导入。");
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

  const activePage = useMemo(
    () => catalogPages.find((page) => page.key === activeKey) ?? catalogPages[0],
    [activeKey],
  );
  const externalBusy =
    catalogQuery.isFetching ||
    saveMutation.isPending ||
    resetMutation.isPending ||
    importJsonMutation.isPending;
  const rows = getRows(draft, activePage.key);
  const excelWorkspace = useReferenceCatalogExcelWorkspace({
    client,
    activeKey,
    activePage,
    draft,
    rows,
    canManage: canManageReferenceCatalog,
    externalBusy,
    markDraft,
    clearMessages: () => { setMessage(null); setSuccessMessage(null); },
    showError: (nextMessage) => { setMessage(nextMessage); setSuccessMessage(null); },
    showSuccess: (nextMessage) => { setMessage(null); setSuccessMessage(nextMessage); },
  });
  const isBusy = excelWorkspace.isBusy;
  const validationErrors = draft ? validateCatalog(draft) : [];
  const canSave = canManageReferenceCatalog && Boolean(draft) && validationErrors.length === 0 && !isBusy;
  const serverDraftSync = useServerDraftSync({
    resourceKey: "single-window-reference-catalog",
    incomingValue: catalogQuery.data?.catalog,
    isDirty: canManageReferenceCatalog && hasUnsavedChanges,
    fingerprint: buildReferenceCatalogSnapshot,
    applyIncoming: (serverCatalog) => {
      setDraft(normalizeCatalog(serverCatalog));
      setHasUnsavedChanges(false);
      setMessage(null);
    },
  });
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: canManageReferenceCatalog && hasUnsavedChanges,
    message: "当前单一窗口参考词典有未保存的修改。",
  });

  useEffect(() => {
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
    const text = await readClipboardText();
    if (text == null) {
      setMessage(getClipboardPasteInstruction("参考数据表格单元格"));
      setSuccessMessage(null);
      return;
    }

    pasteCatalogText(text, focusedCell);
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
    if (!canResetReferenceCatalog || isBusy) {
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
    setSuccessMessage("参考词典已导出。");
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

    if (!await confirmDiscardChanges("导入参考词典配置")) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    importJsonMutation.mutate(file);
  }

  async function handleRefreshCatalog() {
    if (!await confirmDiscardChanges("刷新参考词典")) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    void catalogQuery.refetch();
  }

  return (
    <section className="work-surface single-window-surface single-window-reference-catalog-surface" aria-label="单一窗口参考词典">
      <SingleWindowTabs activeKey="reference-catalog" />
      <input ref={jsonImportInputRef} hidden type="file" accept=".json,application/json" onChange={handleJsonImportFile} />
      <input
        ref={excelWorkspace.inputRef}
        hidden
        type="file"
        accept=".xlsx,.xlsm,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel.sheet.macroEnabled.12"
        onChange={excelWorkspace.handleFile}
      />

      {!canManageReferenceCatalog ? <InlineNotice tone="info">当前账号只能查看申报词典，编辑和导入需要 Operate 权限。</InlineNotice> : null}
      {message ? <InlineNotice tone="error" title="参考词典操作失败">{message}</InlineNotice> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel="单一窗口参考词典"
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {validationErrors.length > 0 ? <InlineNotice tone="warning" title="请检查待导入数据">{validationErrors.slice(0, 4).join("；")}</InlineNotice> : null}

      <ReferenceCatalogToolbar
        activeKey={activePage.key}
        draft={draft}
        rows={rows}
        canManage={canManageReferenceCatalog}
        canReset={canResetReferenceCatalog}
        canSave={canSave}
        isBusy={isBusy}
        onActiveKeyChange={setActiveKey}
        onRefresh={() => void handleRefreshCatalog()}
        onExportJson={handleExportJson}
        onChooseJsonImport={chooseJsonImportFile}
        onChooseExcelImport={excelWorkspace.chooseFile}
        onAddRow={addRow}
        onPaste={() => void pasteFromSystemClipboard()}
        onDeduplicate={deduplicateRows}
        onSave={handleSave}
        onReset={() => void handleReset()}
      />

      <ReferenceCatalogSummary catalog={draft} activeKey={activePage.key} hasUnsavedChanges={hasUnsavedChanges} />

      {canManageReferenceCatalog ? (
        <ReferenceCatalogExcelPanel activePage={activePage} workspace={excelWorkspace} />
      ) : null}

      <ReferenceCatalogTable
        activePage={activePage}
        rows={rows}
        canManage={canManageReferenceCatalog}
        isBusy={isBusy}
        tableFrameRef={tableFrameRef}
        onContextMenu={handleCatalogContextMenu}
        onKeyDown={handleCatalogKeyDown}
        onPaste={handleCatalogPaste}
        onFocusCell={setFocusedCell}
        onUpdateRow={updateRow}
        onDeleteRow={deleteRow}
      />

      {contextMenu ? (
        <ReferenceCatalogContextMenu
          contextMenu={contextMenu}
          activePage={activePage}
          rows={rows}
          canManage={canManageReferenceCatalog}
          isBusy={isBusy}
          onAddRow={addRow}
          onDeleteRow={deleteContextRow}
          onPaste={() => void pasteFromSystemClipboard()}
          onDeduplicate={deduplicateRows}
          onOpenAliasEditor={() => openAliasEditor(contextMenu.cell)}
        />
      ) : null}

      {aliasEditor ? (
        <ReferenceCatalogAliasDialog
          activePage={activePage}
          editor={aliasEditor}
          dialogRef={aliasDialogRef}
          inputRef={aliasEditorInputRef}
          onClose={() => setAliasEditor(null)}
          onChange={(value) => setAliasEditor((current) => current ? { ...current, value } : current)}
          onApply={applyAliasEditor}
        />
      ) : null}
    </section>
  );
}

function buildReferenceCatalogSnapshot(catalog: SingleWindowReferenceCatalogModel) {
  return JSON.stringify(normalizeCatalog(catalog));
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
  downloadBlob(blob, fileName);
}
