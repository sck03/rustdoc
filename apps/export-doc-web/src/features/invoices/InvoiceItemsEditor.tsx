import {
  type ClipboardEvent,
  type FormEvent,
  type KeyboardEvent,
  memo,
  type MouseEvent,
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  ArrowDown,
  ArrowDownToLine,
  ArrowUp,
  ClipboardCopy,
  ClipboardPaste,
  Columns3,
  Copy,
  Eraser,
  PackageCheck,
  PackagePlus,
  PackageSearch,
  Redo2,
  RefreshCw,
  Search,
  Trash2,
  Undo2,
  X,
} from "lucide-react";
import { ApiInvoiceDetailDto, ApiInvoiceItemDto, ApiProductDto, ApiUnitDto, type ExportDocManagerApiClient, type HsCodeKnowledgeSearchItem } from "../../api/index.ts";
import { normalizeText } from "../../ui/formUtils.ts";
import { InvoiceItemHistoryOptionCache } from "./invoiceItemHistory.ts";
import { InvoiceItemShortcutGuide } from "./InvoiceItemShortcutGuide.tsx";
import { InvoiceHsKnowledgePanel } from "./InvoiceHsKnowledgePanel.tsx";
import { formatProductOptionLabel, ProductLibraryPickerDialog } from "./InvoiceProductLibraryPickerDialog.tsx";
import { InvoiceItemCellInput } from "./InvoiceItemCellInput.tsx";
import { InvoiceItemsEditorToolbar } from "./InvoiceItemsEditorToolbar.tsx";
import { InvoiceItemsTable } from "./InvoiceItemsTable.tsx";
import { InvoiceItemsAssist, type UnitCandidateDialogState } from "./InvoiceItemsAssist.tsx";
import { isUnitLookupSourceField, buildUnitCandidateLookup, findChineseUnitCandidates, normalizeUnitEnglishKey, createCellKey, parseCellKey, readSelectedCells, canFillDownSelectedCells, buildCellRangeKeys, getInvoiceItemColumnIndex, normalizeInvoiceItemBlankRowCount, calculateInvoiceItemVirtualRange, buildSelectedCellsClipboardText, calculateInvoiceItemTableMinWidth, getInvoiceItemColumnWidth, readItemClipboardValue, readItemTextValue, sanitizeClipboardCell, writeClipboardText, parseInvoiceItemClipboardRows, createEmptyInvoiceItem, calculateInvoiceTotals, isMeaningfulInvoiceItem } from "./invoiceItemsEditorModel.ts";
import {
  EditableInvoiceItemField,
  firstEditableInvoiceItemField,
  InvoiceItemColumnDefinition,
  invoiceItemEditableColumns,
} from "./invoiceItemTableModel.ts";
export { type EditableInvoiceItemField, type InvoiceItemColumnDefinition, invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
export { calculateInvoiceTotals, createEmptyInvoiceItem, isMeaningfulInvoiceItem, normalizeInvoiceItemForSave, recalculateInvoiceItem } from "./invoiceItemsEditorModel.ts";
const invoiceItemArrowNavigationKeys = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"]);
const blankWhenZeroInvoiceItemNumberFields = new Set<EditableInvoiceItemField>([
  "pcsPerCtn",
  "cartons",
  "length",
  "width",
  "height",
  "volume",
  "gwPerCtn",
  "gwTotal",
  "nwPerCtn",
  "nwTotal",
  "purchasePrice",
  "purchaseTotal",
  "taxRebateRate",
]);

const invoiceItemHeaderHeightPx = 42;
const invoiceItemRowHeightPx = 42;
const invoiceItemVirtualizationThreshold = 90;
const invoiceItemVirtualOverscanRows = 8;

type FocusedInvoiceItemCell = {
  rowIndex: number;
  field: EditableInvoiceItemField;
};

export type InvoiceItemCellSelection = FocusedInvoiceItemCell;

type UnitLookupSourceField = "unitEN" | "ctnUnitEN";
type UnitLookupTargetField = "unitCN" | "ctnUnitCN";

type UnitLookupTarget = {
  sourceField: UnitLookupSourceField;
  targetField: UnitLookupTargetField;
  targetLabel: string;
};

const invoiceItemUnitLookupTargets: Record<UnitLookupSourceField, UnitLookupTarget> = {
  unitEN: {
    sourceField: "unitEN",
    targetField: "unitCN",
    targetLabel: "中文单位",
  },
  ctnUnitEN: {
    sourceField: "ctnUnitEN",
    targetField: "ctnUnitCN",
    targetLabel: "包装中文单位",
  },
};

function isInvoiceItemVerticalNavigationKey(event: KeyboardEvent<HTMLElement>) {
  const nativeEvent = event.nativeEvent as globalThis.KeyboardEvent;
  if (nativeEvent.isComposing || event.ctrlKey || event.altKey || event.metaKey) {
    return false;
  }

  return event.key === "Enter" || event.key === "Tab";
}

function isInvoiceItemCellInputTarget(target: EventTarget | null): target is HTMLInputElement {
  return target instanceof HTMLInputElement && target.classList.contains("item-cell-input");
}

function isInvoiceItemArrowNavigationKey(event: KeyboardEvent<HTMLElement>) {
  const nativeEvent = event.nativeEvent as globalThis.KeyboardEvent;
  if (nativeEvent.isComposing || event.ctrlKey || event.altKey || event.metaKey) {
    return false;
  }

  return invoiceItemArrowNavigationKeys.has(event.key);
}

function shouldMoveInvoiceItemCellByArrow(input: HTMLInputElement, key: string, extendSelection: boolean) {
  if (extendSelection || key === "ArrowUp" || key === "ArrowDown") {
    return true;
  }

  if (key !== "ArrowLeft" && key !== "ArrowRight") {
    return false;
  }

  if (input.type === "number") {
    return true;
  }

  const selectionStart = input.selectionStart ?? 0;
  const selectionEnd = input.selectionEnd ?? selectionStart;
  if (selectionStart !== selectionEnd) {
    return false;
  }

  return key === "ArrowLeft" ? selectionStart <= 0 : selectionEnd >= input.value.length;
}

export function InvoiceItemsEditor({
  client,
  items,
  canRedoItemEdit,
  canSaveToProductLibrary,
  canUseHsKnowledge,
  canUndoItemEdit,
  blankRowCount = 0,
  currency,
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
  onRemoveItem,
  onSaveItemToProductLibrary,
  onSearchProductLibrary,
  onUndoItemEdit,
  productLibraryMessage,
  productLibraryProducts,
  unitLookupMessage,
  unitOptions,
}: {
  client: ExportDocManagerApiClient;
  items: ApiInvoiceItemDto[];
  canRedoItemEdit: boolean;
  canSaveToProductLibrary: boolean;
  canUseHsKnowledge: boolean;
  canUndoItemEdit: boolean;
  blankRowCount?: number;
  currency: string;
  focusedWorkbench?: boolean;
  isProductLibraryBusy: boolean;
  readOnly?: boolean;
  onAddItem: () => void;
  onApplyProductLibraryItem: (product: ApiProductDto, insertAfterIndex: number | null) => void;
  onChangeItem: (index: number, next: Partial<ApiInvoiceItemDto>) => void;
  onClearItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onDuplicateItem: (index: number) => void;
  onFillDownItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onFillDownItemField: (index: number, field: EditableInvoiceItemField) => void;
  onMoveItem: (index: number, direction: -1 | 1) => void;
  onPasteItemTable: (
    startRowIndex: number,
    startField: EditableInvoiceItemField,
    rows: string[][],
    targetFields?: EditableInvoiceItemField[],
  ) => void;
  onRedoItemEdit: () => void;
  onRefreshProductLibrary: () => void;
  onRemoveItem: (index: number) => void;
  onSaveItemToProductLibrary: (index: number) => void;
  onSearchProductLibrary: (keyword: string) => void;
  onUndoItemEdit: () => void;
  productLibraryMessage: string | null;
  productLibraryProducts: ApiProductDto[];
  unitLookupMessage?: string | null;
  unitOptions?: ApiUnitDto[];
}) {
  const [focusedCell, setFocusedCell] = useState<FocusedInvoiceItemCell | null>(null);
  const [editorMessage, setEditorMessage] = useState<string | null>(null);
  const [unitCandidateDialog, setUnitCandidateDialog] = useState<UnitCandidateDialogState | null>(null);
  const [isProductPickerOpen, setIsProductPickerOpen] = useState(false);
  const [isHsKnowledgeOpen, setIsHsKnowledgeOpen] = useState(false);
  const [productKeyword, setProductKeyword] = useState("");
  const [selectedProductId, setSelectedProductId] = useState("");
  const [selectedCellKeys, setSelectedCellKeys] = useState<Set<string>>(new Set());
  const [selectionAnchor, setSelectionAnchor] = useState<FocusedInvoiceItemCell | null>(null);
  const [hiddenColumnFields, setHiddenColumnFields] = useState<Set<EditableInvoiceItemField>>(new Set());
  const [pendingFocusCell, setPendingFocusCell] = useState<FocusedInvoiceItemCell | null>(null);
  const [tableViewport, setTableViewport] = useState({ scrollTop: 0, height: 0 });
  const tableFrameRef = useRef<HTMLDivElement | null>(null);
  const tableScrollFrameRef = useRef<number | null>(null);
  const historyOptionCacheRef = useRef(new InvoiceItemHistoryOptionCache());
  const historyItemsRef = useRef(items);
  const pendingHistoryInvalidationRef = useRef<number | null>(null);
  const visibleColumns = useMemo(
    () => invoiceItemEditableColumns.filter((column) => !hiddenColumnFields.has(column.field)),
    [hiddenColumnFields],
  );
  const unitCandidateLookup = useMemo(() => buildUnitCandidateLookup(unitOptions ?? []), [unitOptions]);
  const focusContextRef = useRef({ selectionAnchor, focusedCell, visibleColumns });
  focusContextRef.current = { selectionAnchor, focusedCell, visibleColumns };
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

  const focusItemCell = useCallback(
    (cell: FocusedInvoiceItemCell, selectionMode: "replace" | "toggle" | "range" = "replace") => {
      const context = focusContextRef.current;
      setFocusedCell(cell);

      if (selectionMode === "range") {
        const anchor = context.selectionAnchor ?? context.focusedCell ?? cell;
        setSelectionAnchor(anchor);
        setSelectedCellKeys(buildCellRangeKeys(anchor, cell, context.visibleColumns));
        return;
      }

      if (selectionMode === "toggle") {
        setSelectionAnchor((current) => current ?? cell);
        setSelectedCellKeys((current) => {
          const next = new Set(current);
          const key = createCellKey(cell);
          if (next.has(key)) {
            next.delete(key);
          } else {
            next.add(key);
          }

          return next.size > 0 ? next : new Set([key]);
        });
        return;
      }

      setSelectionAnchor(cell);
      setSelectedCellKeys(new Set([createCellKey(cell)]));
    },
    [],
  );

  const handleCellMouseDown = useCallback(
    (event: MouseEvent<HTMLInputElement>, cell: FocusedInvoiceItemCell) => {
      if (event.shiftKey) {
        event.preventDefault();
        focusItemCell(cell, "range");
        return;
      }

      if (event.ctrlKey || event.metaKey) {
        event.preventDefault();
        focusItemCell(cell, "toggle");
      }
    },
    [focusItemCell],
  );

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
  const displayRowCount = displayItems.length;
  const shouldVirtualizeRows = displayRowCount > invoiceItemVirtualizationThreshold;
  const virtualRowRange = useMemo(
    () => calculateInvoiceItemVirtualRange(displayRowCount, tableViewport.scrollTop, tableViewport.height, shouldVirtualizeRows),
    [displayRowCount, shouldVirtualizeRows, tableViewport.height, tableViewport.scrollTop],
  );
  const visibleDisplayItems = useMemo(
    () => displayItems.slice(virtualRowRange.startIndex, virtualRowRange.endIndex),
    [displayItems, virtualRowRange.endIndex, virtualRowRange.startIndex],
  );
  const visibleColumnFields = useMemo(() => new Set(visibleColumns.map((column) => column.field)), [visibleColumns]);
  const activeFocusedCell = focusedCell && visibleColumnFields.has(focusedCell.field) ? focusedCell : null;
  const focusedRowIndex = activeFocusedCell?.rowIndex ?? null;
  const totals = useMemo(() => calculateInvoiceTotals(items), [items]);
  const meaningfulItemCount = useMemo(() => items.filter(isMeaningfulInvoiceItem).length, [items]);
  const selectedCells = useMemo(
    () => readSelectedCells(selectedCellKeys, items.length, visibleColumns),
    [items.length, selectedCellKeys, visibleColumns],
  );
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
    const element = tableFrameRef.current;
    if (!element) {
      return;
    }

    updateTableViewportFromElement(element);

    if (typeof ResizeObserver === "undefined") {
      const handleResize = () => updateTableViewportFromElement(element);
      window.addEventListener("resize", handleResize);
      return () => window.removeEventListener("resize", handleResize);
    }

    const observer = new ResizeObserver(() => updateTableViewportFromElement(element));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(
    () => () => {
      if (tableScrollFrameRef.current != null) {
        window.cancelAnimationFrame(tableScrollFrameRef.current);
      }
    },
    [],
  );

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

  useEffect(() => {
    if (
      !pendingFocusCell ||
      pendingFocusCell.rowIndex < 0 ||
      pendingFocusCell.rowIndex >= displayRowCount ||
      !visibleColumnFields.has(pendingFocusCell.field)
    ) {
      return;
    }

    if (
      shouldVirtualizeRows &&
      (pendingFocusCell.rowIndex < virtualRowRange.startIndex || pendingFocusCell.rowIndex >= virtualRowRange.endIndex)
    ) {
      scrollInvoiceItemCellIntoView(pendingFocusCell);
      return;
    }

    focusInvoiceItemInput(pendingFocusCell);
    setPendingFocusCell(null);
  }, [
    displayRowCount,
    pendingFocusCell,
    shouldVirtualizeRows,
    virtualRowRange.endIndex,
    virtualRowRange.startIndex,
    visibleColumnCount,
    visibleColumnFields,
  ]);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === "c" && selectedCellCount > 1) {
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
      return;
    }

    if (isInvoiceItemVerticalNavigationKey(event) && isInvoiceItemCellInputTarget(event.target)) {
      event.preventDefault();
      moveFocusedCellVertically(!event.shiftKey);
      return;
    }

    if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === "z") {
      event.preventDefault();
      if (!readOnly) {
        undoItemEdit();
      }
      return;
    }

    if ((event.ctrlKey && event.key.toLowerCase() === "y") || (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "z")) {
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

    if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === "d") {
      event.preventDefault();
      if (readOnly) {
        return;
      }
      fillDownFocusedCell();
      return;
    }

    if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "d") {
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

    if (!navigator.clipboard?.readText) {
      setEditorMessage("当前环境不能直接读取剪贴板。");
      return;
    }

    try {
      const text = await navigator.clipboard.readText();
      pasteClipboardText(text);
    } catch {
      setEditorMessage("读取剪贴板失败。");
    }
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
    setFocusedCell({ rowIndex: startRowIndex, field: startField });
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

  function moveFocusedCellVertically(moveDown: boolean) {
    if (!activeFocusedCell) {
      return;
    }

    const targetRowIndex = moveDown ? activeFocusedCell.rowIndex + 1 : activeFocusedCell.rowIndex - 1;
    if (targetRowIndex >= displayRowCount) {
      return;
    }

    const nextCell = {
      rowIndex: Math.max(0, targetRowIndex),
      field: activeFocusedCell.field,
    };
    focusItemCellAndInput(nextCell);
    setEditorMessage(null);
  }

  function moveFocusedCellByArrow(key: string, extendSelection: boolean) {
    if (!activeFocusedCell || visibleColumns.length === 0 || displayRowCount === 0) {
      return;
    }

    const currentColumnIndex = getInvoiceItemColumnIndex(activeFocusedCell.field, visibleColumns);
    let nextRowIndex = activeFocusedCell.rowIndex;
    let nextColumnIndex = currentColumnIndex;

    if (key === "ArrowUp") {
      nextRowIndex -= 1;
    } else if (key === "ArrowDown") {
      nextRowIndex += 1;
    } else if (key === "ArrowLeft") {
      nextColumnIndex -= 1;
    } else if (key === "ArrowRight") {
      nextColumnIndex += 1;
    }

    nextRowIndex = Math.max(0, Math.min(displayRowCount - 1, nextRowIndex));
    nextColumnIndex = Math.max(0, Math.min(visibleColumns.length - 1, nextColumnIndex));

    const nextField = visibleColumns[nextColumnIndex]?.field;
    if (!nextField) {
      return;
    }

    const nextCell = { rowIndex: nextRowIndex, field: nextField };
    if (nextCell.rowIndex === activeFocusedCell.rowIndex && nextCell.field === activeFocusedCell.field) {
      return;
    }

    focusItemCellAndInput(nextCell, extendSelection ? "range" : "replace");
    setEditorMessage(null);
  }

  function focusItemCellAndInput(cell: FocusedInvoiceItemCell, selectionMode: "replace" | "range" = "replace") {
    focusItemCell(cell, selectionMode);
    setPendingFocusCell(cell);
    scrollInvoiceItemCellIntoView(cell);
    window.requestAnimationFrame(() => focusInvoiceItemInput(cell));
  }

  function focusInvoiceItemInput(cell: FocusedInvoiceItemCell) {
    const input = tableFrameRef.current?.querySelector<HTMLInputElement>(
      `input[data-invoice-item-row="${cell.rowIndex}"][data-invoice-item-field="${cell.field}"]`,
    );
    input?.focus();
    if (input && input.type !== "number") {
      input.select();
    }
  }

  function updateTableViewportFromElement(element: HTMLDivElement) {
    const nextViewport = {
      scrollTop: element.scrollTop,
      height: element.clientHeight,
    };
    setTableViewport((current) =>
      Math.abs(current.scrollTop - nextViewport.scrollTop) < 1 && current.height === nextViewport.height
        ? current
        : nextViewport,
    );
  }

  function handleTableScroll() {
    const element = tableFrameRef.current;
    if (!element || tableScrollFrameRef.current != null) {
      return;
    }

    tableScrollFrameRef.current = window.requestAnimationFrame(() => {
      tableScrollFrameRef.current = null;
      updateTableViewportFromElement(element);
    });
  }

  function scrollInvoiceItemCellIntoView(cell: FocusedInvoiceItemCell) {
    const element = tableFrameRef.current;
    if (!element || !shouldVirtualizeRows) {
      return;
    }

    const rowTop = invoiceItemHeaderHeightPx + cell.rowIndex * invoiceItemRowHeightPx;
    const rowBottom = rowTop + invoiceItemRowHeightPx;
    const visibleTop = element.scrollTop + invoiceItemHeaderHeightPx;
    const visibleBottom = element.scrollTop + element.clientHeight;
    let nextScrollTop = element.scrollTop;

    if (rowTop < visibleTop) {
      nextScrollTop = Math.max(0, rowTop - invoiceItemHeaderHeightPx);
    } else if (rowBottom > visibleBottom) {
      nextScrollTop = Math.max(0, rowBottom - element.clientHeight);
    }

    if (Math.abs(nextScrollTop - element.scrollTop) >= 1) {
      element.scrollTop = nextScrollTop;
    }
    updateTableViewportFromElement(element);
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
    setFocusedCell(nextCell);
    setPendingFocusCell(nextCell);
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
      setSelectedCellKeys((current) => {
        const next = new Set<string>();
        current.forEach((key) => {
          const cell = parseCellKey(key);
          if (cell && cell.field !== field) {
            next.add(key);
          }
        });
        return next;
      });
      setSelectionAnchor((current) => (current?.field === field ? null : current));
      setFocusedCell((current) => (current?.field === field ? null : current));
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
        onFillDown={fillDownFocusedCell} onOpenProductPicker={() => { setEditorMessage(null); setIsProductPickerOpen(true); }}
        onOpenHsKnowledge={() => { setEditorMessage(null); setIsHsKnowledgeOpen(true); }}
        onPaste={() => void pasteFromClipboardButton()} onProductKeywordChange={setProductKeyword} onProductKeywordKeyDown={handleProductKeywordKeyDown}
        onRedo={redoItemEdit} onRefreshProductLibrary={onRefreshProductLibrary} onSaveFocusedProduct={saveFocusedItemToProductLibrary}
        onSearchProductLibrary={searchProductLibrary} onSelectedProductChange={setSelectedProductId} onShowAllColumns={showAllInvoiceItemColumns}
        onToggleColumn={toggleInvoiceItemColumn} onUndo={undoItemEdit}
      />
      <InvoiceItemShortcutGuide />
      <InvoiceItemsAssist
        focusedRowIndex={focusedRowIndex} isBusy={isProductLibraryBusy} isProductPickerOpen={isProductPickerOpen}
        itemsCount={items.length} productKeyword={productKeyword} products={productLibraryProducts} readOnly={readOnly}
        unitCandidateDialog={unitCandidateDialog} onApplyProduct={applyPickedProduct} onApplyUnitCandidate={applyUnitCandidate}
        onCloseProductPicker={() => setIsProductPickerOpen(false)} onCloseUnitCandidates={() => setUnitCandidateDialog(null)}
        onRefresh={onRefreshProductLibrary} onSearch={(keyword) => { setProductKeyword(keyword); onSearchProductLibrary(keyword); }}
      />
      <InvoiceHsKnowledgePanel
        client={client}
        item={focusedRowIndex == null ? null : items[focusedRowIndex] ?? null}
        open={isHsKnowledgeOpen}
        onClose={() => setIsHsKnowledgeOpen(false)}
        onApply={(patch, result: HsCodeKnowledgeSearchItem) => {
          if (focusedRowIndex == null || readOnly) return;
          markInvoiceItemMutationFrom(focusedRowIndex);
          onChangeItem(focusedRowIndex, patch);
          setEditorMessage(`已回填 HS 编码 ${result.currentCode}；本次确认已进入本地学习记录。`);
        }}
      />
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
