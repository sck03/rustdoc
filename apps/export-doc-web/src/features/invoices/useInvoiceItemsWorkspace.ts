import { useState, type Dispatch, type SetStateAction } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiInvoiceDetailDto, ApiInvoiceItemDto, ApiProductDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { normalizeText, readApiError } from "../../ui/formUtils.ts";
import {
  type InvoiceItemCellSelection,
  calculateInvoiceTotals,
  createEmptyInvoiceItem,
  recalculateInvoiceItem,
} from "./InvoiceItemsEditor.tsx";
import { type EditableInvoiceItemField, invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
import { createInvoiceItemFromProduct, createProductDraftFromInvoiceItem, hasSameProductCode } from "./invoiceProductLibrary.ts";
import {
  areInvoiceItemsEqual,
  areInvoiceItemValuesEqual,
  cloneInvoiceItems,
  readInvoiceItemTableNumber,
} from "./invoiceEditorHelpers.ts";

const maxInvoiceItemHistoryDepth = 50;
type InvoiceItemEditHistory = { redo: ApiInvoiceItemDto[][]; undo: ApiInvoiceItemDto[][] };
const emptyInvoiceItemEditHistory: InvoiceItemEditHistory = { redo: [], undo: [] };

type Options = {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto | null;
  setInvoice: Dispatch<SetStateAction<ApiInvoiceDetailDto | null>>;
  setSuccessMessage: Dispatch<SetStateAction<string | null>>;
  isEditable: boolean;
  canSaveToProductLibrary: boolean;
};

export function useInvoiceItemsWorkspace({
  client,
  invoice,
  setInvoice,
  setSuccessMessage,
  isEditable,
  canSaveToProductLibrary,
}: Options) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [editHistory, setEditHistory] = useState<InvoiceItemEditHistory>(emptyInvoiceItemEditHistory);
  const [productLibraryKeyword, setProductLibraryKeyword] = useState("");
  const [productLibraryMessage, setProductLibraryMessage] = useState<string | null>(null);

  const productsQuery = useQuery({
    queryKey: queryKeys.masterDataList("products", 1, 200, productLibraryKeyword),
    queryFn: () => client.listProducts({ keyword: productLibraryKeyword || undefined }),
    staleTime: 5 * 60 * 1000,
  });
  const saveProductMutation = useMutation({
    mutationFn: async ({ item }: { item: ApiInvoiceItemDto }) => {
      const productCode = normalizeText(item.styleNo);
      if (!productCode) throw new Error("商品编码(款号)不能为空。");

      const candidates = await client.listProducts({ keyword: productCode });
      const existing = candidates.find((product) => hasSameProductCode(product, productCode)) ?? null;
      if (existing && !await requestConfirmation({
        title: "更新商品库",
        description: `商品库中已存在编码为 ${productCode} 的商品，是否用当前发票明细更新？`,
        details: ["只更新商品主数据，不会修改其他历史发票。"],
        confirmLabel: "更新商品",
      })) {
        return { cancelled: true, isUpdate: true, productCode };
      }

      const body = createProductDraftFromInvoiceItem(item, existing);
      if (existing) {
        await client.updateProduct({ id: existing.id, body });
        return { cancelled: false, isUpdate: true, productCode };
      }
      await client.createProduct({ body });
      return { cancelled: false, isUpdate: false, productCode };
    },
    onSuccess: async (result) => {
      if (result.cancelled) {
        setProductLibraryMessage("已取消商品库更新。");
        return;
      }
      setProductLibraryMessage(result.isUpdate ? `商品库已更新：${result.productCode}` : `商品已保存到商品库：${result.productCode}`);
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("products") });
    },
    onError: (error) => setProductLibraryMessage(readApiError(error)),
  });

  function resetEditHistory() {
    setEditHistory(emptyInvoiceItemEditHistory);
  }

  function reset() {
    resetEditHistory();
    setProductLibraryMessage(null);
  }

  function searchProductLibrary(keyword: string) {
    const nextKeyword = normalizeText(keyword);
    setProductLibraryMessage(null);
    setProductLibraryKeyword((current) => {
      if (current === nextKeyword) void productsQuery.refetch();
      return nextKeyword;
    });
  }

  function refreshProductLibrary() {
    setProductLibraryMessage(null);
    void productsQuery.refetch();
  }

  function setInvoiceItems(
    buildItems: (items: ApiInvoiceItemDto[], invoiceId: number) => ApiInvoiceItemDto[],
    options: { trackHistory?: boolean } = { trackHistory: true },
  ) {
    setInvoice((current) => {
      if (!isEditable || !current) return current;
      const currentItems = current.items ?? [];
      const nextItems = buildItems(currentItems, current.id);
      if (areInvoiceItemsEqual(currentItems, nextItems)) return current;

      if (options.trackHistory !== false) {
        const previousItems = cloneInvoiceItems(currentItems);
        setEditHistory((history) => ({
          undo: [...history.undo, previousItems].slice(-maxInvoiceItemHistoryDepth),
          redo: [],
        }));
      }
      return { ...current, items: nextItems, ...calculateInvoiceTotals(nextItems) };
    });
    setSuccessMessage(null);
  }

  function addItem() {
    setInvoiceItems((items, invoiceId) => [...items, createEmptyInvoiceItem(invoiceId)]);
  }

  function applyProductLibraryItem(product: ApiProductDto, insertAfterIndex: number | null) {
    setInvoiceItems((items, invoiceId) => {
      const targetIndex = insertAfterIndex == null || insertAfterIndex < 0 || insertAfterIndex >= items.length
        ? items.length
        : Math.min(items.length, insertAfterIndex + 1);
      return [
        ...items.slice(0, targetIndex),
        createInvoiceItemFromProduct(product, invoiceId),
        ...items.slice(targetIndex),
      ];
    });
    setProductLibraryMessage(`已从商品库新增明细：${normalizeText(product.productCode) || product.id}`);
  }

  function saveItemToProductLibrary(index: number) {
    if (!canSaveToProductLibrary) {
      setProductLibraryMessage("当前权限只能读取商品库，不能新增或更新商品资料。");
      return;
    }
    const item = invoice?.items?.[index];
    if (!item) {
      setProductLibraryMessage("请先选择一行要保存的商品明细。");
      return;
    }
    if (!normalizeText(item.styleNo)) {
      setProductLibraryMessage("商品编码(款号)不能为空。");
      return;
    }
    setProductLibraryMessage(null);
    saveProductMutation.mutate({ item: { ...item } });
  }

  function duplicateItem(index: number) {
    setInvoiceItems((items, invoiceId) => {
      const source = items[index];
      if (!source) return items;
      const duplicated = { ...createEmptyInvoiceItem(invoiceId), ...source, id: 0, invoiceId };
      return [...items.slice(0, index + 1), duplicated, ...items.slice(index + 1)];
    });
  }

  function moveItem(index: number, direction: -1 | 1) {
    setInvoiceItems((items) => {
      const targetIndex = index + direction;
      if (index < 0 || index >= items.length || targetIndex < 0 || targetIndex >= items.length) return items;
      const nextItems = [...items];
      [nextItems[index], nextItems[targetIndex]] = [nextItems[targetIndex], nextItems[index]];
      return nextItems;
    });
  }

  function fillDownItemField(index: number, field: EditableInvoiceItemField) {
    setInvoiceItems((items, invoiceId) => {
      const source = items[index - 1];
      const target = items[index];
      if (!source || !target) return items;
      const patch = { [field]: source[field] } as Partial<ApiInvoiceItemDto>;
      return items.map((item, itemIndex) => itemIndex === index
        ? recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...patch }, [field])
        : item);
    });
  }

  function fillDownItemCells(cells: InvoiceItemCellSelection[]) {
    if (cells.length < 2) return;
    setInvoiceItems((items, invoiceId) => {
      const rowsByField = new Map<EditableInvoiceItemField, Set<number>>();
      for (const cell of cells) {
        if (cell.rowIndex < 0 || cell.rowIndex >= items.length) continue;
        const rows = rowsByField.get(cell.field) ?? new Set<number>();
        rows.add(cell.rowIndex);
        rowsByField.set(cell.field, rows);
      }

      const patchesByRow = new Map<number, Partial<ApiInvoiceItemDto>>();
      const changedFieldsByRow = new Map<number, Set<EditableInvoiceItemField>>();
      rowsByField.forEach((rowSet, field) => {
        const rowIndices = Array.from(rowSet).sort((left, right) => left - right);
        if (rowIndices.length < 2) return;
        const source = items[rowIndices[0]];
        if (!source) return;
        const sourceValue = source[field];
        rowIndices.slice(1).forEach((rowIndex) => {
          const target = items[rowIndex];
          if (!target || areInvoiceItemValuesEqual(target[field], sourceValue)) return;
          const patch = patchesByRow.get(rowIndex) ?? {};
          (patch as Record<string, unknown>)[field] = sourceValue;
          patchesByRow.set(rowIndex, patch);
          const fields = changedFieldsByRow.get(rowIndex) ?? new Set<EditableInvoiceItemField>();
          fields.add(field);
          changedFieldsByRow.set(rowIndex, fields);
        });
      });
      if (patchesByRow.size === 0) return items;
      return items.map((item, itemIndex) => {
        const patch = patchesByRow.get(itemIndex);
        const fields = changedFieldsByRow.get(itemIndex);
        return patch && fields
          ? recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...patch }, Array.from(fields))
          : item;
      });
    });
  }

  function pasteItemTable(
    startRowIndex: number,
    startField: EditableInvoiceItemField,
    rows: string[][],
    targetFields = invoiceItemEditableColumns.map((column) => column.field),
  ) {
    const targetColumns = targetFields
      .map((field) => invoiceItemEditableColumns.find((column) => column.field === field))
      .filter((column): column is (typeof invoiceItemEditableColumns)[number] => Boolean(column));
    const startColumnIndex = targetColumns.findIndex((column) => column.field === startField);
    if (startColumnIndex < 0 || rows.length === 0) return;

    setInvoiceItems((items, invoiceId) => {
      const nextItems = [...items];
      rows.forEach((row, rowOffset) => {
        const targetIndex = Math.max(0, startRowIndex) + rowOffset;
        const current = nextItems[targetIndex] ?? createEmptyInvoiceItem(invoiceId);
        const patch: Partial<ApiInvoiceItemDto> = {};
        const changedFields: string[] = [];
        row.forEach((cell, colOffset) => {
          const column = targetColumns[startColumnIndex + colOffset];
          if (!column) return;
          (patch as Partial<Record<EditableInvoiceItemField, string | number | undefined>>)[column.field] =
            column.kind === "number" ? readInvoiceItemTableNumber(cell) : cell.trim();
          changedFields.push(column.field);
        });
        if (changedFields.length === 0) return;
        nextItems[targetIndex] = recalculateInvoiceItem({
          ...createEmptyInvoiceItem(invoiceId),
          ...current,
          ...patch,
          id: current.id ?? 0,
          invoiceId,
        }, changedFields);
      });
      return nextItems;
    });
  }

  function patchItem(index: number, next: Partial<ApiInvoiceItemDto>) {
    const changedFields = Object.keys(next);
    setInvoiceItems((items, invoiceId) => {
      if (index < 0) return items;
      const nextItems = [...items];
      while (nextItems.length <= index) nextItems.push(createEmptyInvoiceItem(invoiceId));
      return nextItems.map((item, itemIndex) => itemIndex === index
        ? recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...next }, changedFields)
        : item);
    });
  }

  function clearItemCells(cells: InvoiceItemCellSelection[]) {
    if (cells.length === 0) return;
    setInvoiceItems((items, invoiceId) => {
      const cellsByRow = new Map<number, Set<EditableInvoiceItemField>>();
      cells.forEach((cell) => {
        if (cell.rowIndex < 0 || cell.rowIndex >= items.length) return;
        const fields = cellsByRow.get(cell.rowIndex) ?? new Set<EditableInvoiceItemField>();
        fields.add(cell.field);
        cellsByRow.set(cell.rowIndex, fields);
      });
      if (cellsByRow.size === 0) return items;
      return items.map((item, itemIndex) => {
        const fields = cellsByRow.get(itemIndex);
        if (!fields?.size) return item;
        const patch: Partial<ApiInvoiceItemDto> = {};
        const changedFields = Array.from(fields);
        changedFields.forEach((field) => {
          const column = invoiceItemEditableColumns.find((entry) => entry.field === field);
          (patch as Partial<Record<EditableInvoiceItemField, string | number | undefined>>)[field] =
            column?.kind === "number" ? undefined : "";
        });
        return recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...patch }, changedFields);
      });
    });
  }

  function removeItem(index: number) {
    setInvoiceItems((items) => items.filter((_, itemIndex) => itemIndex !== index));
  }
  function applyItemsSnapshot(items: ApiInvoiceItemDto[]) {
    setInvoiceItems(() => cloneInvoiceItems(items), { trackHistory: false });
  }
  function undoItemEdit() {
    if (!invoice || editHistory.undo.length === 0) return;
    const previousItems = editHistory.undo[editHistory.undo.length - 1];
    const currentItems = cloneInvoiceItems(invoice.items ?? []);
    applyItemsSnapshot(previousItems);
    setEditHistory({
      undo: editHistory.undo.slice(0, -1),
      redo: [...editHistory.redo, currentItems].slice(-maxInvoiceItemHistoryDepth),
    });
    setSuccessMessage(null);
  }
  function redoItemEdit() {
    if (!invoice || editHistory.redo.length === 0) return;
    const nextItems = editHistory.redo[editHistory.redo.length - 1];
    const currentItems = cloneInvoiceItems(invoice.items ?? []);
    applyItemsSnapshot(nextItems);
    setEditHistory({
      undo: [...editHistory.undo, currentItems].slice(-maxInvoiceItemHistoryDepth),
      redo: editHistory.redo.slice(0, -1),
    });
    setSuccessMessage(null);
  }

  return {
    products: productsQuery.data ?? [],
    productLibraryMessage: productsQuery.isError ? readApiError(productsQuery.error) : productLibraryMessage,
    isProductLibraryBusy: productsQuery.isFetching || saveProductMutation.isPending,
    canUndoItemEdit: editHistory.undo.length > 0,
    canRedoItemEdit: editHistory.redo.length > 0,
    reset,
    resetEditHistory,
    searchProductLibrary,
    refreshProductLibrary,
    addItem,
    applyProductLibraryItem,
    saveItemToProductLibrary,
    duplicateItem,
    moveItem,
    fillDownItemField,
    fillDownItemCells,
    pasteItemTable,
    patchItem,
    clearItemCells,
    removeItem,
    undoItemEdit,
    redoItemEdit,
  };
}

export type InvoiceItemsWorkspace = ReturnType<typeof useInvoiceItemsWorkspace>;
