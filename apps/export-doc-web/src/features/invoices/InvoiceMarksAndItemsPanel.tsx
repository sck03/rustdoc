import { useEffect, useState } from "react";
import { FileText, Image as ImageIcon, Maximize2, Pencil, Plus } from "lucide-react";
import type {
  ApiInvoiceDetailDto,
  ApiInvoiceItemDto,
  ApiProductDto,
  ApiUnitDto,
  ExportDocManagerApiClient,
  HsCodeKnowledgeFeedbackInput,
} from "../../api/index.ts";
import { TextAreaField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { type InvoiceItemCellSelection, InvoiceItemsEditor } from "./InvoiceItemsEditor.tsx";
import type { EditableInvoiceItemField } from "./invoiceItemTableModel.ts";
import { ShippingMarkEditorDialog } from "./ShippingMarkEditorDialog.tsx";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

export function InvoiceMarksAndItemsPanel({
  client,
  invoice,
  canRedoItemEdit,
  canSaveToProductLibrary,
  canUseHsKnowledge,
  canUndoItemEdit,
  invoiceItemBlankRowCount,
  isEditable,
  isFocusedWorkbench = false,
  isProductLibraryBusy,
  onChange,
  onAddItem,
  onApplyProductLibraryItem,
  onChangeItem,
  onClearItemCells,
  onDuplicateItem,
  onFillDownItemCells,
  onFillDownItemField,
  onMoveItem,
  onOpenFocusedWorkbench,
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
}: {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto;
  canRedoItemEdit: boolean;
  canSaveToProductLibrary: boolean;
  canUseHsKnowledge: boolean;
  canUndoItemEdit: boolean;
  invoiceItemBlankRowCount: number;
  isEditable: boolean;
  isFocusedWorkbench?: boolean;
  isProductLibraryBusy: boolean;
  onChange: (next: InvoicePatch) => void;
  onAddItem: () => void;
  onApplyProductLibraryItem: (product: ApiProductDto, insertAfterIndex: number | null) => void;
  onChangeItem: (index: number, next: Partial<ApiInvoiceItemDto>) => void;
  onClearItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onDuplicateItem: (index: number) => void;
  onFillDownItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onFillDownItemField: (index: number, field: EditableInvoiceItemField) => void;
  onMoveItem: (index: number, direction: -1 | 1) => void;
  onOpenFocusedWorkbench?: () => void;
  onPasteItemTable: (
    startRowIndex: number,
    startField: EditableInvoiceItemField,
    rows: string[][],
    targetFields?: EditableInvoiceItemField[],
  ) => void;
  onRedoItemEdit: () => void;
  onRefreshProductLibrary: () => void;
  onOpenProductLibrary: () => void;
  onRemoveItem: (index: number) => void;
  onSaveItemToProductLibrary: (index: number) => void;
  onSearchProductLibrary: (keyword: string) => void;
  onUndoItemEdit: () => void;
  onHsKnowledgeFeedback: (feedback: HsCodeKnowledgeFeedbackInput) => void;
  productLibraryMessage: string | null;
  productLibraryProducts: ApiProductDto[];
  productLibraryPageNumber: number;
  productLibraryPageSize: number;
  productLibraryTotalCount: number;
  productLibraryTotalPages: number;
  onProductLibraryPageChange: (pageNumber: number) => void;
  onProductLibraryPageSizeChange: (pageSize: number) => void;
  unitLookupMessage: string | null;
  unitOptions: ApiUnitDto[];
}) {
  const [isShippingMarkEditorOpen, setIsShippingMarkEditorOpen] = useState(false);
  const [isShippingMarkSaving, setIsShippingMarkSaving] = useState(false);
  const [isShippingMarkPreviewBusy, setIsShippingMarkPreviewBusy] = useState(false);
  const [shippingMarkPreviewDataUrl, setShippingMarkPreviewDataUrl] = useState<string | null>(null);
  const [shippingMarkMessage, setShippingMarkMessage] = useState<string | null>(null);

  const shippingMarksMode = invoice.shippingMarksType?.trim() === "Image" ? "Image" : "Text";
  const shippingMarksImagePath = invoice.shippingMarksImage?.trim() ?? "";

  useEffect(() => {
    if (shippingMarksMode !== "Image" || !shippingMarksImagePath) {
      setShippingMarkPreviewDataUrl(null);
      setIsShippingMarkPreviewBusy(false);
      return;
    }

    let isCancelled = false;
    setIsShippingMarkPreviewBusy(true);
    setShippingMarkMessage(null);

    client
      .previewShippingMarkImage({ body: { imagePath: shippingMarksImagePath } })
      .then((response) => {
        if (isCancelled) {
          return;
        }

        setShippingMarkPreviewDataUrl(response.dataUrl);
        setShippingMarkMessage(null);
      })
      .catch((error) => {
        if (isCancelled) {
          return;
        }

        setShippingMarkPreviewDataUrl(null);
        setShippingMarkMessage(readApiError(error));
      })
      .finally(() => {
        if (!isCancelled) {
          setIsShippingMarkPreviewBusy(false);
        }
      });

    return () => {
      isCancelled = true;
    };
  }, [client, shippingMarksImagePath, shippingMarksMode]);

  function changeShippingMarksMode(nextMode: "Text" | "Image") {
    if (nextMode === shippingMarksMode) {
      return;
    }

    setShippingMarkMessage(null);
    onChange({ shippingMarksType: nextMode });
  }

  async function saveShippingMarkImage(imageDataUrl: string) {
    setIsShippingMarkSaving(true);
    setShippingMarkMessage(null);

    try {
      const response = await client.saveShippingMarkImage({ body: { imageDataUrl } });
      setShippingMarkPreviewDataUrl(imageDataUrl);
      onChange({
        shippingMarks: "",
        shippingMarksType: "Image",
        shippingMarksImage: response.imagePath,
      });
      setShippingMarkMessage("唛头图片已保存。");
      setIsShippingMarkEditorOpen(false);
    } catch (error) {
      setShippingMarkMessage(readApiError(error));
      throw error;
    } finally {
      setIsShippingMarkSaving(false);
    }
  }

  const supportFields = (
    <>
      <div className="shipping-mark-field invoice-items-support-panel">
        <div className="shipping-mark-mode-row">
          <span className="shipping-mark-field-title">唛头</span>
          <div className="segmented-control shipping-mark-mode-control" role="group" aria-label="唛头类型">
            <button
              type="button"
              className={shippingMarksMode === "Text" ? "segmented-active" : ""}
              disabled={!isEditable}
              onClick={() => changeShippingMarksMode("Text")}
            >
              <FileText size={15} aria-hidden="true" />
              <span>文本</span>
            </button>
            <button
              type="button"
              className={shippingMarksMode === "Image" ? "segmented-active" : ""}
              disabled={!isEditable}
              onClick={() => changeShippingMarksMode("Image")}
            >
              <ImageIcon size={15} aria-hidden="true" />
              <span>图片</span>
            </button>
          </div>
        </div>
        {shippingMarksMode === "Text" ? (
          <label className="textarea-field shipping-mark-textarea-field">
            <textarea
              value={invoice.shippingMarks ?? ""}
              disabled={!isEditable}
              onChange={(event) => onChange({ shippingMarks: event.target.value, shippingMarksType: "Text" })}
            />
          </label>
        ) : (
          <div className="shipping-mark-image-panel">
            <div className="shipping-mark-preview-frame">
              {isShippingMarkPreviewBusy ? <span>加载中</span> : null}
              {!isShippingMarkPreviewBusy && shippingMarkPreviewDataUrl ? (
                <img src={shippingMarkPreviewDataUrl} alt="唛头图片" />
              ) : null}
              {!isShippingMarkPreviewBusy && !shippingMarkPreviewDataUrl ? <span>未设置图片</span> : null}
            </div>
            <div className="shipping-mark-image-actions">
              <button
                className="command-button secondary"
                type="button"
                disabled={!isEditable || isShippingMarkSaving}
                onClick={() => {
                  setShippingMarkMessage(null);
                  setIsShippingMarkEditorOpen(true);
                }}
              >
                <Pencil size={16} aria-hidden="true" />
                <span>编辑图片</span>
              </button>
              {shippingMarksImagePath ? <span className="shipping-mark-image-path">{shippingMarksImagePath}</span> : null}
            </div>
          </div>
        )}
        {shippingMarkMessage ? <div className="item-editor-message shipping-mark-message">{shippingMarkMessage}</div> : null}
      </div>
      <TextAreaField
        className="invoice-special-terms-field"
        label="特殊条款"
        value={invoice.specialTerms ?? ""}
        disabled={!isEditable}
        onChange={(value) => onChange({ specialTerms: value })}
      />
    </>
  );

  return (
    <section
      className={isFocusedWorkbench ? "form-section invoice-items-workbench invoice-items-focus-panel information-tier-required" : "form-section invoice-items-workbench information-tier-required"}
      aria-label="商品明细"
    >
      <div className="section-header">
        <div>
          <h2>商品明细</h2>
          <span>{invoice.items?.length ?? 0} 行已录入</span>
        </div>
        <div className="toolbar-actions invoice-items-header-actions">
          {!isFocusedWorkbench && onOpenFocusedWorkbench ? (
            <button className="command-button secondary" type="button" onClick={onOpenFocusedWorkbench}>
              <Maximize2 size={16} aria-hidden="true" />
              <span>明细工作台</span>
            </button>
          ) : null}
          <button className="icon-button" type="button" title="新增商品明细" aria-label="新增商品明细" disabled={!isEditable} onClick={onAddItem}>
            <Plus size={17} aria-hidden="true" />
          </button>
        </div>
      </div>
      <details className="invoice-items-support-details">
        <summary>唛头与特殊条款</summary>
        <div className="invoice-items-support-details-body">{supportFields}</div>
      </details>
      <InvoiceItemsEditor
        client={client}
        items={invoice.items}
        canRedoItemEdit={canRedoItemEdit}
        canSaveToProductLibrary={canSaveToProductLibrary}
        canUseHsKnowledge={canUseHsKnowledge}
        canUndoItemEdit={canUndoItemEdit}
        blankRowCount={invoiceItemBlankRowCount}
        currency={invoice.currency}
        exchangeRate={invoice.exchangeRate}
        isProductLibraryBusy={isProductLibraryBusy}
        readOnly={!isEditable}
        onAddItem={onAddItem}
        onApplyProductLibraryItem={onApplyProductLibraryItem}
        onChangeItem={onChangeItem}
        onClearItemCells={onClearItemCells}
        onDuplicateItem={onDuplicateItem}
        onFillDownItemCells={onFillDownItemCells}
        onFillDownItemField={onFillDownItemField}
        onMoveItem={onMoveItem}
        onPasteItemTable={onPasteItemTable}
        onRedoItemEdit={onRedoItemEdit}
        onRefreshProductLibrary={onRefreshProductLibrary}
        onOpenProductLibrary={onOpenProductLibrary}
        onRemoveItem={onRemoveItem}
        onSaveItemToProductLibrary={onSaveItemToProductLibrary}
        onSearchProductLibrary={onSearchProductLibrary}
        onUndoItemEdit={onUndoItemEdit}
        onHsKnowledgeFeedback={onHsKnowledgeFeedback}
        productLibraryMessage={productLibraryMessage}
        productLibraryProducts={productLibraryProducts}
        productLibraryPageNumber={productLibraryPageNumber}
        productLibraryPageSize={productLibraryPageSize}
        productLibraryTotalCount={productLibraryTotalCount}
        productLibraryTotalPages={productLibraryTotalPages}
        onProductLibraryPageChange={onProductLibraryPageChange}
        onProductLibraryPageSizeChange={onProductLibraryPageSizeChange}
        focusedWorkbench={isFocusedWorkbench}
        unitLookupMessage={unitLookupMessage}
        unitOptions={unitOptions}
      />
      {isShippingMarkEditorOpen ? (
        <ShippingMarkEditorDialog
          initialImageDataUrl={shippingMarkPreviewDataUrl}
          isSaving={isShippingMarkSaving}
          message={shippingMarkMessage}
          onClose={() => setIsShippingMarkEditorOpen(false)}
          onSave={saveShippingMarkImage}
        />
      ) : null}
    </section>
  );
}
