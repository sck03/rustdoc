import {
  type MouseEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import type { ApiInvoiceItemDto } from "../../api/index.ts";
import {
  invoiceItemHeaderHeightPx,
  invoiceItemRowHeightPx,
  invoiceItemVirtualizationThreshold,
} from "./invoiceItemsEditorInteraction.ts";
import {
  buildCellRangeKeys,
  calculateInvoiceItemVirtualRange,
  createCellKey,
  getInvoiceItemColumnIndex,
  parseCellKey,
  readSelectedCells,
} from "./invoiceItemsEditorModel.ts";
import type { InvoiceItemColumnDefinition, EditableInvoiceItemField } from "./invoiceItemTableModel.ts";
import type { InvoiceItemCellSelection } from "./invoiceItemsEditorTypes.ts";

type SelectionMode = "replace" | "toggle" | "range";

type UseInvoiceItemsGridInteractionOptions = {
  displayItems: ApiInvoiceItemDto[];
  itemsLength: number;
  visibleColumns: InvoiceItemColumnDefinition[];
};

export function useInvoiceItemsGridInteraction({
  displayItems,
  itemsLength,
  visibleColumns,
}: UseInvoiceItemsGridInteractionOptions) {
  const [focusedCell, setFocusedCell] = useState<InvoiceItemCellSelection | null>(null);
  const [selectedCellKeys, setSelectedCellKeys] = useState<Set<string>>(new Set());
  const [selectionAnchor, setSelectionAnchor] = useState<InvoiceItemCellSelection | null>(null);
  const [pendingFocusCell, setPendingFocusCell] = useState<InvoiceItemCellSelection | null>(null);
  const [tableViewport, setTableViewport] = useState({ scrollTop: 0, height: 0 });
  const tableFrameRef = useRef<HTMLDivElement | null>(null);
  const tableScrollFrameRef = useRef<number | null>(null);
  const focusContextRef = useRef({ selectionAnchor, focusedCell, visibleColumns });
  focusContextRef.current = { selectionAnchor, focusedCell, visibleColumns };

  const displayRowCount = displayItems.length;
  const shouldVirtualizeRows = displayRowCount > invoiceItemVirtualizationThreshold;
  const virtualRowRange = useMemo(
    () => calculateInvoiceItemVirtualRange(
      displayRowCount,
      tableViewport.scrollTop,
      tableViewport.height,
      shouldVirtualizeRows,
    ),
    [displayRowCount, shouldVirtualizeRows, tableViewport.height, tableViewport.scrollTop],
  );
  const visibleDisplayItems = useMemo(
    () => displayItems.slice(virtualRowRange.startIndex, virtualRowRange.endIndex),
    [displayItems, virtualRowRange.endIndex, virtualRowRange.startIndex],
  );
  const visibleColumnFields = useMemo(
    () => new Set(visibleColumns.map((column) => column.field)),
    [visibleColumns],
  );
  const activeFocusedCell = focusedCell && visibleColumnFields.has(focusedCell.field) ? focusedCell : null;
  const selectedCells = useMemo(
    () => readSelectedCells(selectedCellKeys, itemsLength, visibleColumns),
    [itemsLength, selectedCellKeys, visibleColumns],
  );

  const focusItemCell = useCallback(
    (cell: InvoiceItemCellSelection, selectionMode: SelectionMode = "replace") => {
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
    (event: MouseEvent<HTMLInputElement>, cell: InvoiceItemCellSelection) => {
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

  const updateTableViewportFromElement = useCallback((element: HTMLDivElement) => {
    const nextViewport = {
      scrollTop: element.scrollTop,
      height: element.clientHeight,
    };
    setTableViewport((current) =>
      Math.abs(current.scrollTop - nextViewport.scrollTop) < 1 && current.height === nextViewport.height
        ? current
        : nextViewport,
    );
  }, []);

  const handleTableScroll = useCallback(() => {
    const element = tableFrameRef.current;
    if (!element || tableScrollFrameRef.current != null) {
      return;
    }

    tableScrollFrameRef.current = window.requestAnimationFrame(() => {
      tableScrollFrameRef.current = null;
      updateTableViewportFromElement(element);
    });
  }, [updateTableViewportFromElement]);

  const scrollInvoiceItemCellIntoView = useCallback(
    (cell: InvoiceItemCellSelection) => {
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
    },
    [shouldVirtualizeRows, updateTableViewportFromElement],
  );

  const focusInvoiceItemInput = useCallback((cell: InvoiceItemCellSelection) => {
    const input = tableFrameRef.current?.querySelector<HTMLInputElement>(
      `input[data-invoice-item-row="${cell.rowIndex}"][data-invoice-item-field="${cell.field}"]`,
    );
    input?.focus();
    if (input && input.type !== "number") {
      input.select();
    }
  }, []);

  const focusItemCellAndInput = useCallback(
    (cell: InvoiceItemCellSelection, selectionMode: Exclude<SelectionMode, "toggle"> = "replace") => {
      focusItemCell(cell, selectionMode);
      setPendingFocusCell(cell);
      scrollInvoiceItemCellIntoView(cell);
      window.requestAnimationFrame(() => focusInvoiceItemInput(cell));
    },
    [focusInvoiceItemInput, focusItemCell, scrollInvoiceItemCellIntoView],
  );

  const moveFocusedCellVertically = useCallback(
    (moveDown: boolean) => {
      if (!activeFocusedCell) {
        return;
      }

      const targetRowIndex = moveDown ? activeFocusedCell.rowIndex + 1 : activeFocusedCell.rowIndex - 1;
      if (targetRowIndex >= displayRowCount) {
        return;
      }

      focusItemCellAndInput({
        rowIndex: Math.max(0, targetRowIndex),
        field: activeFocusedCell.field,
      });
    },
    [activeFocusedCell, displayRowCount, focusItemCellAndInput],
  );

  const moveFocusedCellByArrow = useCallback(
    (key: string, extendSelection: boolean) => {
      if (!activeFocusedCell || visibleColumns.length === 0 || displayRowCount === 0) {
        return;
      }

      const currentColumnIndex = getInvoiceItemColumnIndex(activeFocusedCell.field, visibleColumns);
      let nextRowIndex = activeFocusedCell.rowIndex;
      let nextColumnIndex = currentColumnIndex;

      if (key === "ArrowUp") nextRowIndex -= 1;
      else if (key === "ArrowDown") nextRowIndex += 1;
      else if (key === "ArrowLeft") nextColumnIndex -= 1;
      else if (key === "ArrowRight") nextColumnIndex += 1;

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
    },
    [activeFocusedCell, displayRowCount, focusItemCellAndInput, visibleColumns],
  );

  const removeFieldFromSelection = useCallback((field: EditableInvoiceItemField) => {
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
    setPendingFocusCell((current) => (current?.field === field ? null : current));
  }, []);

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
  }, [updateTableViewportFromElement]);

  useEffect(
    () => () => {
      if (tableScrollFrameRef.current != null) {
        window.cancelAnimationFrame(tableScrollFrameRef.current);
      }
    },
    [],
  );

  useEffect(() => {
    if (!pendingFocusCell || pendingFocusCell.rowIndex < 0 || !visibleColumnFields.has(pendingFocusCell.field)) {
      return;
    }
    if (pendingFocusCell.rowIndex >= displayRowCount) {
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
    focusInvoiceItemInput,
    pendingFocusCell,
    scrollInvoiceItemCellIntoView,
    shouldVirtualizeRows,
    virtualRowRange.endIndex,
    virtualRowRange.startIndex,
    visibleColumnFields,
  ]);

  return {
    activeFocusedCell,
    displayRowCount,
    focusItemCell,
    focusItemCellAndInput,
    handleCellMouseDown,
    handleTableScroll,
    moveFocusedCellByArrow,
    moveFocusedCellVertically,
    removeFieldFromSelection,
    selectedCellKeys,
    selectedCells,
    shouldVirtualizeRows,
    tableFrameRef,
    virtualRowRange,
    visibleDisplayItems,
  };
}
