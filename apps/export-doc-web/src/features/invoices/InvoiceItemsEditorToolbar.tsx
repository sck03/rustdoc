import type { KeyboardEvent } from "react";
import { ArrowDownToLine, ClipboardCopy, ClipboardPaste, Columns3, Eraser, PackageCheck, PackagePlus, PackageSearch, Redo2, RefreshCw, Search, Sparkles, Undo2 } from "lucide-react";
import type { ApiProductDto } from "../../api/index.ts";
import type { EditableInvoiceItemField } from "./invoiceItemTableModel.ts";
import { invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
import { formatProductOptionLabel } from "./InvoiceProductLibraryPickerDialog.tsx";

type Props = {
 canApplySelectedProduct:boolean; canRedoItemEdit:boolean; canSaveFocusedItem:boolean; canUndoItemEdit:boolean; hiddenColumnFields:Set<EditableInvoiceItemField>;
 canUseHsKnowledge:boolean; isFillDownAvailable:boolean; isProductLibraryBusy:boolean; productKeyword:string; productLibraryProducts:ApiProductDto[]; readOnly:boolean;
 selectedCellCount:number; selectedProductId:string; visibleColumnCount:number; visibleMessage:string|null;
 onApplySelectedProduct():void; onClearSelectedCells():void; onCopySelectedCells():void; onFillDown():void; onOpenProductPicker():void; onPaste():void;
 onProductKeywordChange(value:string):void; onProductKeywordKeyDown(event:KeyboardEvent<HTMLInputElement>):void; onRedo():void; onRefreshProductLibrary():void;
 onOpenHsKnowledge():void; onSaveFocusedProduct():void; onSearchProductLibrary():void; onSelectedProductChange(value:string):void; onShowAllColumns():void;
 onToggleColumn(field:EditableInvoiceItemField):void; onUndo():void;
};

export function InvoiceItemsEditorToolbar(props:Props){
 const {canApplySelectedProduct,canRedoItemEdit,canSaveFocusedItem,canUndoItemEdit,canUseHsKnowledge,hiddenColumnFields,isFillDownAvailable,isProductLibraryBusy,productKeyword,productLibraryProducts,readOnly,selectedCellCount,selectedProductId,visibleColumnCount,visibleMessage}=props;
 const {onApplySelectedProduct:applySelectedProduct,onClearSelectedCells:clearSelectedCells,onCopySelectedCells,onFillDown:fillDownFocusedCell,onOpenHsKnowledge,onOpenProductPicker,onPaste,onProductKeywordChange:setProductKeyword,onProductKeywordKeyDown:handleProductKeywordKeyDown,onRedo:redoItemEdit,onRefreshProductLibrary,onSaveFocusedProduct:saveFocusedItemToProductLibrary,onSearchProductLibrary:searchProductLibrary,onSelectedProductChange:setSelectedProductId,onShowAllColumns:showAllInvoiceItemColumns,onToggleColumn:toggleInvoiceItemColumn,onUndo:undoItemEdit}=props;
 return (
      <div className="item-editor-toolbar" aria-label="明细编辑工具">
        <div className="item-product-library-toolbar" aria-label="商品库工具">
          <PackageSearch size={16} aria-hidden="true" />
          <input
            className="item-product-library-search"
            aria-label="商品库搜索"
            placeholder="商品库搜索"
            value={productKeyword}
            onChange={(event) => setProductKeyword(event.target.value)}
            onKeyDown={handleProductKeywordKeyDown}
          />
          <button
            className="icon-button compact-icon-button"
            type="button"
            title="搜索商品库"
            aria-label="搜索商品库"
            disabled={isProductLibraryBusy}
            onClick={searchProductLibrary}
          >
            <Search size={16} aria-hidden="true" />
          </button>
          <button
            className="icon-button compact-icon-button"
            type="button"
            title="打开商品库选择"
            aria-label="打开商品库选择"
            disabled={isProductLibraryBusy}
            onClick={onOpenProductPicker}
          >
            <PackageSearch size={16} aria-hidden="true" />
          </button>
          <select
            className="item-product-library-select"
            aria-label="商品库商品"
            value={selectedProductId}
            disabled={isProductLibraryBusy || productLibraryProducts.length === 0}
            onChange={(event) => setSelectedProductId(event.target.value)}
          >
            <option value="">商品库</option>
            {productLibraryProducts.map((product) => (
              <option key={product.id} value={product.id}>
                {formatProductOptionLabel(product)}
              </option>
            ))}
          </select>
          <button
            className="icon-button compact-icon-button"
            type="button"
            title="从商品库新增明细"
            aria-label="从商品库新增明细"
            disabled={isProductLibraryBusy || readOnly || !canApplySelectedProduct}
            onClick={applySelectedProduct}
          >
            <PackagePlus size={16} aria-hidden="true" />
          </button>
          <button
            className="icon-button compact-icon-button"
            type="button"
            title="保存当前明细到商品库"
            aria-label="保存当前明细到商品库"
            disabled={isProductLibraryBusy || readOnly || !canSaveFocusedItem}
            onClick={saveFocusedItemToProductLibrary}
          >
            <PackageCheck size={16} aria-hidden="true" />
          </button>
          <button
            className="icon-button compact-icon-button"
            type="button"
            title="刷新商品库"
            aria-label="刷新商品库"
            disabled={isProductLibraryBusy}
            onClick={onRefreshProductLibrary}
          >
            <RefreshCw size={16} aria-hidden="true" />
          </button>
          {canUseHsKnowledge ? <button className="secondary-button invoice-hs-open-button" type="button" disabled={isProductLibraryBusy} onClick={onOpenHsKnowledge}><Sparkles size={15}/><span>智能 HS</span></button> : null}
        </div>
        <details className="item-column-visibility-menu">
          <summary className="icon-button compact-icon-button" title="显示/隐藏明细列" aria-label="显示/隐藏明细列">
            <Columns3 size={16} aria-hidden="true" />
          </summary>
          <div className="item-column-menu" role="menu" aria-label="明细显示列">
            <div className="item-column-menu-header">
              <span>显示列</span>
              <button type="button" className="text-button compact-text-button" onClick={showAllInvoiceItemColumns}>
                全部显示
              </button>
            </div>
            <div className="item-column-menu-list">
              {invoiceItemEditableColumns.map((column) => {
                const checked = !hiddenColumnFields.has(column.field);
                return (
                  <label className="item-column-option" key={column.field}>
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={checked && visibleColumnCount <= 1}
                      onChange={() => toggleInvoiceItemColumn(column.field)}
                    />
                    <span>{column.header}</span>
                  </label>
                );
              })}
            </div>
          </div>
        </details>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="复制选中单元格"
          aria-label="复制选中单元格"
          disabled={selectedCellCount === 0}
          onClick={onCopySelectedCells}
        >
          <ClipboardCopy size={16} aria-hidden="true" />
        </button>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="清空选中单元格"
          aria-label="清空选中单元格"
          disabled={readOnly || selectedCellCount === 0}
          onClick={clearSelectedCells}
        >
          <Eraser size={16} aria-hidden="true" />
        </button>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="从剪贴板粘贴明细"
          aria-label="从剪贴板粘贴明细"
          disabled={readOnly}
          onClick={onPaste}
        >
          <ClipboardPaste size={16} aria-hidden="true" />
        </button>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="向下填充当前单元格"
          aria-label="向下填充当前单元格"
          disabled={readOnly || !isFillDownAvailable}
          onClick={fillDownFocusedCell}
        >
          <ArrowDownToLine size={16} aria-hidden="true" />
        </button>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="撤销明细编辑"
          aria-label="撤销明细编辑"
          disabled={readOnly || !canUndoItemEdit}
          onClick={undoItemEdit}
        >
          <Undo2 size={16} aria-hidden="true" />
        </button>
        <button
          className="icon-button compact-icon-button"
          type="button"
          title="重做明细编辑"
          aria-label="重做明细编辑"
          disabled={readOnly || !canRedoItemEdit}
          onClick={redoItemEdit}
        >
          <Redo2 size={16} aria-hidden="true" />
        </button>
        {visibleMessage ? <span className="item-editor-message">{visibleMessage}</span> : null}
      </div>

 );
}
