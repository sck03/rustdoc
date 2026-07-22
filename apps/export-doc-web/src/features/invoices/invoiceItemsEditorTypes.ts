import type { ApiInvoiceItemDto, ApiProductDto, ApiUnitDto, ExportDocManagerApiClient } from "../../api/index.ts";
import type { EditableInvoiceItemField } from "./invoiceItemTableModel.ts";

export type InvoiceItemCellSelection = { rowIndex: number; field: EditableInvoiceItemField };
export type InvoiceItemsEditorProps = {
  client: ExportDocManagerApiClient; items: ApiInvoiceItemDto[]; canRedoItemEdit: boolean; canSaveToProductLibrary: boolean; canUseHsKnowledge: boolean; canUndoItemEdit: boolean;
  blankRowCount?: number; currency: string; focusedWorkbench?: boolean; isProductLibraryBusy: boolean; readOnly?: boolean;
  onAddItem(): void; onApplyProductLibraryItem(product: ApiProductDto, insertAfterIndex: number | null): void; onChangeItem(index: number, next: Partial<ApiInvoiceItemDto>): void;
  onClearItemCells(cells: InvoiceItemCellSelection[]): void; onDuplicateItem(index: number): void; onFillDownItemCells(cells: InvoiceItemCellSelection[]): void;
  onFillDownItemField(index: number, field: EditableInvoiceItemField): void; onMoveItem(index: number, direction: -1 | 1): void;
  onPasteItemTable(startRowIndex: number, startField: EditableInvoiceItemField, rows: string[][], targetFields?: EditableInvoiceItemField[]): void;
  onRedoItemEdit(): void; onRefreshProductLibrary(): void; onRemoveItem(index: number): void; onSaveItemToProductLibrary(index: number): void;
  onSearchProductLibrary(keyword: string): void; onOpenProductLibrary(): void; onUndoItemEdit(): void; productLibraryMessage: string | null; productLibraryProducts: ApiProductDto[];
  productLibraryPageNumber: number; productLibraryPageSize: number; productLibraryTotalCount: number; productLibraryTotalPages: number;
  onProductLibraryPageChange(pageNumber: number): void; onProductLibraryPageSizeChange(pageSize: number): void;
  unitLookupMessage?: string | null; unitOptions?: ApiUnitDto[];
};
