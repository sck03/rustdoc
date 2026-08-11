import {
  type ClipboardEvent,
  type KeyboardEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { ApiInvoiceItemDto, ApiProductDto } from "../../api/index.ts";
import { normalizeText } from "../../ui/formUtils.ts";
import { getClipboardPasteInstruction, readClipboardText, writeClipboardText } from "../../ui/clipboard.ts";
import { InvoiceItemHistoryOptionCache } from "./invoiceItemHistory.ts";
import { InvoiceItemShortcutGuide } from "./InvoiceItemShortcutGuide.tsx";
import { InvoiceItemsEditorToolbar } from "./InvoiceItemsEditorToolbar.tsx";
import { InvoiceItemsTable } from "./InvoiceItemsTable.tsx";
import { InvoiceItemsEditorDialogs } from "./InvoiceItemsEditorDialogs.tsx";
import { useInvoiceItemsEditorInteraction } from "./useInvoiceItemsEditorInteraction.ts";
import { invoiceItemUnitLookupTargets, isInvoiceItemArrowNavigationKey, isInvoiceItemCellInputTarget, isInvoiceItemVerticalNavigationKey, shouldMoveInvoiceItemCellByArrow } from "./invoiceItemsEditorInteraction.ts";
import { isUnitLookupSourceField, buildUnitCandidateLookup, findChineseUnitCandidates, normalizeUnitEnglishKey, canFillDownSelectedCells, normalizeInvoiceItemBlankRowCount, buildSelectedCellsClipboardText, calculateInvoiceItemTableMinWidth, readItemTextValue, parseInvoiceItemClipboardRows, createEmptyInvoiceItem, calculateInvoiceTotals, isMeaningfulInvoiceItem } from "./invoiceItemsEditorModel.ts";
import {
  EditableInvoiceItemField,
  firstEditableInvoiceItemField,
  InvoiceItemColumnDefinition,
  invoiceItemEditableColumns,
} from "./invoiceItemTableModel.ts";
export { type EditableInvoiceItemField, type InvoiceItemColumnDefinition, invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
export { calculateInvoiceTotals, createEmptyInvoiceItem, isMeaningfulInvoiceItem, normalizeInvoiceItemForSave, recalculateInvoiceItem } from "./invoiceItemsEditorModel.ts";
import type { InvoiceItemsEditorProps } from "./invoiceItemsEditorTypes.ts";
import { useInvoiceItemsGridInteraction } from "./useInvoiceItemsGridInteraction.ts";
export type { InvoiceItemCellSelection } from "./invoiceItemsEditorTypes.ts";

export function InvoiceItemsEditor({
  client,
  items,
  canRedoItemEdit,
  canSaveToProductLibrary,
  canUseHsKnowledge,
  canUndoItemEdit,
  blankRowCount = 0,
  currency,
  exchangeRate,
  focusedWorkbench = false,
  isProductLibraryBusy,
  readOnly = false,
  onAddItem,
  onApplyProductLibraryItem,
  onChangeItem,
  onClearItemCells,
  onDuplicateItem,
  onFillDownItemCells,
  onFillDownItemField,
  onMoveItem,
  onPasteItemTable,
  onRedoItemEdit,
  onRefreshProductLibrary,
  onOpenProductLibrary,
  onRemoveItem,
  onSaveItemToProductLibrary,
  onSearchProductLibrary,
  onUndoItemEdit,
  onHsKnowledgeFeedback,
  productLibraryMessage,
  productLibraryProducts,
  productLibraryPageNumber,
  productLibraryPageSize,
  productLibraryTotalCount,
  productLibraryTotalPages,
  onProductLibraryPageChange,
  onProductLibraryPageSizeChange,
  unitLookupMessage,
  unitOptions,
}: InvoiceItemsEditorProps) {
  const [editorMessage, setEditorMessage] = useState<string | null>(null);
  const { unitCandidateDialog, setUnitCandidateDialog, isProductPickerOpen, setIsProductPickerOpen, isHsKnowledgeOpen, setIsHsKnowledgeOpen, productKeyword, setProductKeyword, selectedProductId, setSelectedProductId, hiddenColumnFields, setHiddenColumnFields } = useInvoiceItemsEditorInteraction();
  const historyOptionCacheRef = useRef(new InvoiceItemHistoryOptionCache());
  const historyItemsRef = useRef(items);
  const pendingHistoryInvalidationRef = useRef<number | null>(null);
  const visibleColumns = useMemo(
    () => invoiceItemEditableColumns.filter((column) => !hiddenColumnFields.has(column.field)),
    [hiddenColumnFields],
  );
  const unitCandidateLookup = useMemo(() => buildUnitCandidateLookup(unitOptions ?? []), [unitOptions]);
  const itemEditContextRef = useRef({ onChangeItem, readOnly, unitCandidateLookup });
  itemEditContextRef.current = { onChangeItem, readOnly, unitCandidateLookup };

  if (historyItemsRef.current !== items) {
    const changedFromRow = pendingHistoryInvalidationRef.current;
    if (changedFromRow == null) {
      historyOptionCacheRef.current.clear();
    } else {
      historyOptionCacheRef.current.invalidateAfter(changedFromRow);
    }

    historyItemsRef.current = items;
    pendingHistoryInvalidationRef.current = null;
  }

  const markInvoiceItemMutationFrom = useCallback((rowIndex: number) => {
    const normalizedRowIndex = Math.max(0, Math.trunc(rowIndex));
    pendingHistoryInvalidationRef.current =
      pendingHistoryInvalidationRef.current == null
        ? normalizedRowIndex
        : Math.min(pendingHistoryInvalidationRef.current, normalizedRowIndex);
  }, []);

  const updateItemField = useCallback(
    (index: number, column: InvoiceItemColumnDefinition, value: string | number | undefined) => {
      const context = itemEditContextRef.current;
      if (context.readOnly) {
        return;
      }

      markInvoiceItemMutationFrom(index);
      if (isUnitLookupSourceField(column.field) && typeof value === "string") {
        const target = invoiceItemUnitLookupTargets[column.field];
        const unitEn = normalizeText(value);
        const candidates = findChineseUnitCandidates(context.unitCandidateLookup, unitEn);

        if (candidates.length === 1) {
          context.onChangeItem(index, {
            [column.field]: value,
            [target.targetField]: candidates[0],
          } as Partial<ApiInvoiceItemDto>);
          setUnitCandidateDialog((current) =>
            current?.rowIndex === index && current.sourceField === column.field ? null : current,
          );
          setEditorMessage(`已回填${target.targetLabel}：${candidates[0]}`);
          return;
        }

        context.onChangeItem(index, { [column.field]: value } as Partial<ApiInvoiceItemDto>);
        if (candidates.length > 1) {
          setUnitCandidateDialog({
            ...target,
            rowIndex: index,
            unitEn,
            unitEnKey: normalizeUnitEnglishKey(unitEn),
            candidates,
          });
        } else {
          setUnitCandidateDialog((current) =>
            current?.rowIndex === index && current.sourceField === column.field ? null : current,
          );
        }
        setEditorMessage(null);
        return;
      }

      context.onChangeItem(index, { [column.field]: value } as Partial<ApiInvoiceItemDto>);
      setUnitCandidateDialog((current) =>
        current?.rowIndex === index && current.sourceField === column.field ? null : current,
      );
      setEditorMessage(null);
    },
    [markInvoiceItemMutationFrom],
  );
  const normalizedBlankRowCount = readOnly ? 0 : normalizeInvoiceItemBlankRowCount(blankRowCount);
  const blankDisplayRows = useMemo(
    () => Array.from({ length: normalizedBlankRowCount }, () => createEmptyInvoiceItem(0)),
    [normalizedBlankRowCount],
  );
  const displayItems = useMemo(
    () => (blankDisplayRows.length > 0 ? [...items, ...blankDisplayRows] : items),
    [blankDisplayRows, items],
  );
  const {
    activeFocusedCell,
    focusItemCell,
    focusItemCellAndInput,
    handleCellMouseDown,
    handleTableScroll,
    moveFocusedCellByArrow,
    moveFocusedCellVertically,
    removeFieldFromSelection,
    selectedCellKeys,
    selectedCells,
    tableFrameRef,
    virtualRowRange,
    visibleDisplayItems,
  } = useInvoiceItemsGridInteraction({
    displayItems,
    itemsLength: items.length,
    visibleColumns,
  });
  const focusedRowIndex = activeFocusedCell?.rowIndex ?? null;
  const totals = useMemo(
    () => calculateInvoiceTotals(items, exchangeRate, currency),
    [currency, exchangeRate, items],
  );
  const meaningfulItemCount = useMemo(() => items.filter(isMeaningfulInvoiceItem).length, [items]);
  const hasFillDownSelection = canFillDownSelectedCells(selectedCells);
  const isFillDownAvailable =
    hasFillDownSelection || Boolean(activeFocusedCell && activeFocusedCell.rowIndex > 0 && activeFocusedCell.rowIndex < items.length);
  const selectedCellCount = selectedCells.length;
  const selectedProductIdNumber = Number(selectedProductId);
  const canApplySelectedProduct =
    Number.isInteger(selectedProductIdNumber) &&
    selectedProductIdNumber > 0 &&
    productLibraryProducts.some((product) => product.id === selectedProductIdNumber);
  const canSaveFocusedItem = canSaveToProductLibrary && focusedRowIndex != null && focusedRowIndex >= 0 && focusedRowIndex < items.length;
  const visibleMessage = editorMessage ?? productLibraryMessage ?? unitLookupMessage ?? null;
  const invoiceItemTableMinWidth = calculateInvoiceItemTableMinWidth(visibleColumns);
  const visibleColumnCount = visibleColumns.length;
  const activeFocusedColumn = activeFocusedCell
    ? visibleColumns.find((column) => column.field === activeFocusedCell.field)
    : undefined;
  const activeFocusedCellOptions =
    activeFocusedCell && activeFocusedColumn
      ? historyOptionCacheRef.current.getOptions(items, activeFocusedCell.rowIndex, activeFocusedColumn)
      : [];

  useEffect(() => {
    if (readOnly || visibleColumns.length === 0) {
      return;
    }

    const targetRows = Array.from(new Set([Math.max(0, items.length - 1), items.length]));
    const warmTargets = targetRows.flatMap((rowIndex) => visibleColumns.map((column) => ({ rowIndex, column })));
    let nextTargetIndex = 0;
    let timeoutId = 0;
    let cancelled = false;

    const warmNextTarget = () => {
      if (cancelled || nextTargetIndex >= warmTargets.length) {
        return;
      }

      const target = warmTargets[nextTargetIndex];
      nextTargetIndex += 1;
      historyOptionCacheRef.current.getOptions(items, target.rowIndex, target.column);
      timeoutId = window.setTimeout(warmNextTarget, 0);
    };

    timeoutId = window.setTimeout(warmNextTarget, 0);
    return () => {
      cancelled = true;
      window.clearTimeout(timeoutId);
    };
  }, [items, readOnly, visibleColumns]);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const primaryModifier = event.ctrlKey || event.metaKey;
    if (primaryModifier && !event.shiftKey && event.key.toLowerCase() === "c" && selectedCellCount > 1) {
      event.preventDefault();
      void copySelectedCells();
      return;
    }

    if (event.key === "Delete" && selectedCellCount > 1) {
      event.preventDefault();
      if (!readOnly) {
        clearSelectedCells();
      }
      return;
    }

    if (
      isInvoiceItemArrowNavigationKey(event) &&
      isInvoiceItemCellInputTarget(event.target) &&
      shouldMoveInvoiceItemCellByArrow(event.target, event.key, event.shiftKey)
    ) {
      event.preventDefault();
      moveFocusedCellByArrow(event.key, event.shiftKey);
      setEditorMessage(null);
      return;
    }

    if (isInvoiceItemVerticalNavigationKey(event) && isInvoiceItemCellInputTarget(event.target)) {
      event.preventDefault();
      moveFocusedCellVertically(!event.shiftKey);
      setEditorMessage(null);
      return;
    }

    if (primaryModifier && !event.shiftKey && event.key.toLowerCase() === "z") {
      event.preventDefault();
      if (!readOnly) {
        undoItemEdit();
      }
      return;
    }

    if ((primaryModifier && event.key.toLowerCase() === "y") || (primaryModifier && event.shiftKey && event.key.toLowerCase() === "z")) {
      event.preventDefault();
      if (!readOnly) {
        redoItemEdit();
      }
      return;
    }

    if (event.key === "Insert") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      markInvoiceItemMutationFrom(items.length);
      onAddItem();
      focusItemCellAndInput({ rowIndex: items.length, field: firstEditableInvoiceItemField });
      return;
    }

    if (focusedRowIndex == null || focusedRowIndex < 0 || focusedRowIndex >= items.length) {
      return;
    }

    if (primaryModifier && !event.shiftKey && event.key.toLowerCase() === "d") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      fillDownFocusedCell();
      return;
    }

    if (primaryModifier && event.shiftKey && event.key.toLowerCase() === "d") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      markInvoiceItemMutationFrom(focusedRowIndex);
      onDuplicateItem(focusedRowIndex);
      focusItemCellAndInput({ rowIndex: focusedRowIndex + 1, field: activeFocusedCell?.field ?? firstEditableInvoiceItemField });
      return;
    }

    if (event.altKey && event.key === "ArrowUp") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      markInvoiceItemMutationFrom(Math.max(0, focusedRowIndex - 1));
      onMoveItem(focusedRowIndex, -1);
      focusItemCellAndInput({
        rowIndex: Math.max(0, focusedRowIndex - 1),
        field: activeFocusedCell?.field ?? firstEditableInvoiceItemField,
      });
      return;
    }

    if (event.altKey && event.key === "ArrowDown") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      markInvoiceItemMutationFrom(focusedRowIndex);
      onMoveItem(focusedRowIndex, 1);
      focusItemCellAndInput({
        rowIndex: Math.min(items.length - 1, focusedRowIndex + 1),
        field: activeFocusedCell?.field ?? firstEditableInvoiceItemField,
      });
    }
  }

  function handlePaste(event: ClipboardEvent<HTMLDivElement>) {
    if (readOnly) {
      return;
    }

    const text = event.clipboardData.getData("text");
    if (!text) {
      return;
    }

    event.preventDefault();
    pasteClipboardText(text);
  }

  async function pasteFromClipboardButton() {
    if (readOnly) {
      return;
    }

    const text = await readClipboardText();
    if (text == null) {
      setEditorMessage(getClipboardPasteInstruction("商品明细表格"));
      return;
    }

    pasteClipboardText(text);
  }

  function pasteClipboardText(text: string) {
    if (readOnly) {
      return;
    }

    const rows = parseInvoiceItemClipboardRows(text);
    if (rows.length === 0) {
      setEditorMessage(null);
      return;
    }

    const startRowIndex = activeFocusedCell?.rowIndex ?? items.length;
    const startField = activeFocusedCell?.field ?? visibleColumns[0]?.field ?? firstEditableInvoiceItemField;
    markInvoiceItemMutationFrom(startRowIndex);
    onPasteItemTable(
      startRowIndex,
      startField,
      rows,
      visibleColumns.map((column) => column.field),
    );
    focusItemCell({ rowIndex: startRowIndex, field: startField });
    setEditorMessage(`${rows.length} 行剪贴板内容已应用。`);
  }

  function fillDownFocusedCell() {
    if (readOnly) {
      return;
    }

    if (hasFillDownSelection) {
      markInvoiceItemMutationFrom(Math.min(...selectedCells.map((cell) => cell.rowIndex)));
      onFillDownItemCells(selectedCells);
      setEditorMessage("已按选区向下填充。");
      return;
    }

    if (!activeFocusedCell || activeFocusedCell.rowIndex <= 0) {
      return;
    }

    markInvoiceItemMutationFrom(activeFocusedCell.rowIndex);
    onFillDownItemField(activeFocusedCell.rowIndex, activeFocusedCell.field);
    setEditorMessage("已向下填充当前单元格。");
  }

  async function copySelectedCells() {
    const text = buildSelectedCellsClipboardText(selectedCells, items, visibleColumns);
    if (!text) {
      return;
    }

    const copied = await writeClipboardText(text);
    setEditorMessage(copied ? `已复制 ${selectedCellCount} 个单元格。` : "复制选区失败。");
  }

  function clearSelectedCells() {
    if (readOnly || selectedCells.length === 0) {
      return;
    }

    markInvoiceItemMutationFrom(Math.min(...selectedCells.map((cell) => cell.rowIndex)));
    onClearItemCells(selectedCells);
    setEditorMessage(`已清空 ${selectedCells.length} 个单元格。`);
  }

  function undoItemEdit() {
    if (readOnly || !canUndoItemEdit) {
      return;
    }

    markInvoiceItemMutationFrom(0);
    onUndoItemEdit();
    setEditorMessage("已撤销明细编辑。");
  }

  function redoItemEdit() {
    if (readOnly || !canRedoItemEdit) {
      return;
    }

    markInvoiceItemMutationFrom(0);
    onRedoItemEdit();
    setEditorMessage("已重做明细编辑。");
  }

  function searchProductLibrary() {
    onSearchProductLibrary(productKeyword);
    setEditorMessage(null);
  }

  function handleProductKeywordKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== "Enter") {
      return;
    }

    event.preventDefault();
    searchProductLibrary();
  }

  function insertProductLibraryItem(product: ApiProductDto, successMessage: string) {
    if (readOnly) {
      return;
    }

    const nextCell = {
      rowIndex: focusedRowIndex == null ? items.length : Math.min(items.length, focusedRowIndex + 1),
      field: firstEditableInvoiceItemField,
    };
    markInvoiceItemMutationFrom(Math.max(0, nextCell.rowIndex - 1));
    onApplyProductLibraryItem(product, focusedRowIndex);
    focusItemCellAndInput(nextCell);
    setEditorMessage(successMessage);
  }

  function applySelectedProduct() {
    if (readOnly || !canApplySelectedProduct) {
      return;
    }

    const product = productLibraryProducts.find((item) => item.id === selectedProductIdNumber);
    if (!product) {
      setEditorMessage("请选择要套用的商品。");
      return;
    }

    insertProductLibraryItem(product, "已从商品库新增明细。");
  }

  function applyPickedProduct(product: ApiProductDto) {
    if (readOnly) {
      return;
    }

    setSelectedProductId(String(product.id));
    insertProductLibraryItem(product, "已从商品库选择新增明细。");
    setIsProductPickerOpen(false);
  }

  function saveFocusedItemToProductLibrary() {
    if (readOnly || !canSaveFocusedItem || focusedRowIndex == null) {
      setEditorMessage("请先选择要保存的明细行。");
      return;
    }

    onSaveItemToProductLibrary(focusedRowIndex);
    setEditorMessage(null);
  }

  function toggleInvoiceItemColumn(field: EditableInvoiceItemField) {
    const column = invoiceItemEditableColumns.find((entry) => entry.field === field);
    const isHidden = hiddenColumnFields.has(field);
    if (!isHidden && visibleColumnCount <= 1) {
      setEditorMessage("至少保留 1 个明细列。");
      return;
    }

    setHiddenColumnFields((current) => {
      const next = new Set(current);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }

      return next;
    });

    if (!isHidden) {
      removeFieldFromSelection(field);
    }

    setEditorMessage(`${isHidden ? "已显示" : "已隐藏"}${column?.header ?? "明细"}列。`);
  }

  function showAllInvoiceItemColumns() {
    setHiddenColumnFields(new Set<EditableInvoiceItemField>());
    setEditorMessage("已显示全部明细列。");
  }

  function applyUnitCandidate(candidate: string) {
    if (!unitCandidateDialog || readOnly) {
      return;
    }

    const currentItem = items[unitCandidateDialog.rowIndex];
    if (!currentItem || normalizeUnitEnglishKey(readItemTextValue(currentItem, unitCandidateDialog.sourceField)) !== unitCandidateDialog.unitEnKey) {
      setUnitCandidateDialog(null);
      setEditorMessage("英文单位已变化，请重新选择。");
      return;
    }

    markInvoiceItemMutationFrom(unitCandidateDialog.rowIndex);
    onChangeItem(unitCandidateDialog.rowIndex, {
      [unitCandidateDialog.targetField]: candidate,
    } as Partial<ApiInvoiceItemDto>);
    setUnitCandidateDialog(null);
    setEditorMessage(`已回填${unitCandidateDialog.targetLabel}：${candidate}`);
  }

  return (
    <div className={focusedWorkbench ? "item-editor-layout item-editor-layout-focused" : "item-editor-layout"}>
      <InvoiceItemsEditorToolbar
        canApplySelectedProduct={canApplySelectedProduct} canRedoItemEdit={canRedoItemEdit} canSaveFocusedItem={canSaveFocusedItem}
        canUndoItemEdit={canUndoItemEdit} canUseHsKnowledge={!readOnly && canUseHsKnowledge && focusedRowIndex != null && focusedRowIndex < items.length}
        hiddenColumnFields={hiddenColumnFields} isFillDownAvailable={isFillDownAvailable}
        isProductLibraryBusy={isProductLibraryBusy} productKeyword={productKeyword} productLibraryProducts={productLibraryProducts}
        readOnly={readOnly} selectedCellCount={selectedCellCount} selectedProductId={selectedProductId}
        visibleColumnCount={visibleColumnCount} visibleMessage={visibleMessage}
        onApplySelectedProduct={applySelectedProduct} onClearSelectedCells={clearSelectedCells} onCopySelectedCells={() => void copySelectedCells()}
        onFillDown={fillDownFocusedCell} onOpenProductPicker={() => { setEditorMessage(null); onOpenProductLibrary(); setIsProductPickerOpen(true); }}
        onOpenHsKnowledge={() => { setEditorMessage(null); setIsHsKnowledgeOpen(true); }}
        onPaste={() => void pasteFromClipboardButton()} onProductKeywordChange={setProductKeyword} onProductKeywordKeyDown={handleProductKeywordKeyDown}
        onRedo={redoItemEdit} onRefreshProductLibrary={onRefreshProductLibrary} onSaveFocusedProduct={saveFocusedItemToProductLibrary}
        onSearchProductLibrary={searchProductLibrary} onSelectedProductChange={setSelectedProductId} onShowAllColumns={showAllInvoiceItemColumns}
        onToggleColumn={toggleInvoiceItemColumn} onUndo={undoItemEdit}
      />
      <InvoiceItemShortcutGuide />
      <InvoiceItemsEditorDialogs client={client} focusedRowIndex={focusedRowIndex} isBusy={isProductLibraryBusy}
        isProductPickerOpen={isProductPickerOpen} isHsKnowledgeOpen={isHsKnowledgeOpen} items={items} productKeyword={productKeyword}
        products={productLibraryProducts} productLibraryPageNumber={productLibraryPageNumber} productLibraryPageSize={productLibraryPageSize} productLibraryTotalCount={productLibraryTotalCount} productLibraryTotalPages={productLibraryTotalPages} readOnly={readOnly} unitCandidateDialog={unitCandidateDialog} onApplyProduct={applyPickedProduct}
        onApplyUnitCandidate={applyUnitCandidate} onCloseProductPicker={() => setIsProductPickerOpen(false)} onCloseUnitCandidates={() => setUnitCandidateDialog(null)}
        onRefresh={onRefreshProductLibrary} onSearch={(keyword) => { setProductKeyword(keyword); onSearchProductLibrary(keyword); }} onProductLibraryPageChange={onProductLibraryPageChange} onProductLibraryPageSizeChange={onProductLibraryPageSizeChange}
        onCloseHsKnowledge={() => setIsHsKnowledgeOpen(false)} onApplyHs={(patch, result, pendingFeedback) => { if (focusedRowIndex == null || readOnly) return; markInvoiceItemMutationFrom(focusedRowIndex); onChangeItem(focusedRowIndex, patch); onHsKnowledgeFeedback(pendingFeedback); setEditorMessage(`已回填 HS 编码 ${result.currentCode}；确认记录将在发票保存时一并提交。`); }} />
      <InvoiceItemsTable
        activeFocusedCell={activeFocusedCell} activeFocusedCellOptions={activeFocusedCellOptions} currency={currency}
        displayItems={displayItems} invoiceItemTableMinWidth={invoiceItemTableMinWidth} itemsCount={items.length}
        meaningfulItemCount={meaningfulItemCount} readOnly={readOnly} selectedCellKeys={selectedCellKeys}
        tableFrameRef={tableFrameRef} totals={totals} virtualRowRange={virtualRowRange}
        visibleColumns={visibleColumns} visibleDisplayItems={visibleDisplayItems}
        onCellMouseDown={handleCellMouseDown} onDuplicateItem={onDuplicateItem} onFocusCell={focusItemCell}
        onKeyDown={handleKeyDown} onMarkMutation={markInvoiceItemMutationFrom} onMoveItem={onMoveItem}
        onPaste={handlePaste} onRemoveItem={onRemoveItem} onScroll={handleTableScroll} onUpdateItemField={updateItemField}
      />
    </div>
  );
}
