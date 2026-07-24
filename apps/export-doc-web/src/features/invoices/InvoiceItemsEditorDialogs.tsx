import type { ApiInvoiceItemDto, ApiProductDto, ExportDocManagerApiClient, HsCodeKnowledgeSearchItem } from "../../api/index.ts";
import { InvoiceHsKnowledgePanel } from "./InvoiceHsKnowledgePanel.tsx";
import { InvoiceItemsAssist, type UnitCandidateDialogState } from "./InvoiceItemsAssist.tsx";

type Props = {
  client: ExportDocManagerApiClient; focusedRowIndex: number | null; isBusy: boolean; isProductPickerOpen: boolean; isHsKnowledgeOpen: boolean;
  items: ApiInvoiceItemDto[]; productKeyword: string; products: ApiProductDto[]; productLibraryPageNumber: number; productLibraryPageSize: number; productLibraryTotalCount: number; productLibraryTotalPages: number; readOnly: boolean; unitCandidateDialog: UnitCandidateDialogState | null;
  onApplyProduct(product: ApiProductDto): void; onApplyUnitCandidate(candidate: string): void; onCloseProductPicker(): void; onCloseUnitCandidates(): void;
  onRefresh(): void; onSearch(keyword: string): void; onProductLibraryPageChange(pageNumber: number): void; onProductLibraryPageSizeChange(pageSize: number): void; onCloseHsKnowledge(): void; onApplyHs(patch: Partial<ApiInvoiceItemDto>, result: HsCodeKnowledgeSearchItem, feedbackRecorded: boolean): void;
};

export function InvoiceItemsEditorDialogs(props: Props) {
  return <>
    <InvoiceItemsAssist focusedRowIndex={props.focusedRowIndex} isBusy={props.isBusy} isProductPickerOpen={props.isProductPickerOpen}
      itemsCount={props.items.length} productKeyword={props.productKeyword} products={props.products} productLibraryPageNumber={props.productLibraryPageNumber} productLibraryPageSize={props.productLibraryPageSize} productLibraryTotalCount={props.productLibraryTotalCount} productLibraryTotalPages={props.productLibraryTotalPages} readOnly={props.readOnly}
      unitCandidateDialog={props.unitCandidateDialog} onApplyProduct={props.onApplyProduct} onApplyUnitCandidate={props.onApplyUnitCandidate}
      onCloseProductPicker={props.onCloseProductPicker} onCloseUnitCandidates={props.onCloseUnitCandidates} onRefresh={props.onRefresh} onSearch={props.onSearch} onProductLibraryPageChange={props.onProductLibraryPageChange} onProductLibraryPageSizeChange={props.onProductLibraryPageSizeChange} />
    <InvoiceHsKnowledgePanel client={props.client} item={props.focusedRowIndex == null ? null : props.items[props.focusedRowIndex] ?? null}
      open={props.isHsKnowledgeOpen} onClose={props.onCloseHsKnowledge} onApply={props.onApplyHs} />
  </>;
}
