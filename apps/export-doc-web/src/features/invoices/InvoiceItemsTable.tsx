import type { ClipboardEvent, KeyboardEvent, MouseEvent, RefObject, UIEvent } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ApiInvoiceDetailDto, ApiInvoiceItemDto } from "../../api/index.ts";
import { formatAmount, formatPlainNumber } from "../../ui/formUtils.ts";
import { InvoiceItemCellInput } from "./InvoiceItemCellInput.tsx";
import { createCellKey, type InvoiceItemCellSelection } from "./invoiceItemsEditorModel.ts";
import { firstEditableInvoiceItemField, type EditableInvoiceItemField, type InvoiceItemColumnDefinition } from "./invoiceItemTableModel.ts";

type VirtualRange={startIndex:number;endIndex:number;topSpacerHeight:number;bottomSpacerHeight:number};
type Props={activeFocusedCell:InvoiceItemCellSelection|null;activeFocusedCellOptions?:string[];currency?:string;displayItems:ApiInvoiceItemDto[];invoiceItemTableMinWidth:number;itemsCount:number;meaningfulItemCount:number;readOnly:boolean;selectedCellKeys:Set<string>;tableFrameRef:RefObject<HTMLDivElement>;totals:Partial<ApiInvoiceDetailDto>;virtualRowRange:VirtualRange;visibleColumns:InvoiceItemColumnDefinition[];visibleDisplayItems:ApiInvoiceItemDto[];onCellMouseDown(event:MouseEvent<HTMLInputElement>,cell:InvoiceItemCellSelection):void;onDuplicateItem(index:number):void;onFocusCell(cell:InvoiceItemCellSelection):void;onKeyDown(event:KeyboardEvent<HTMLDivElement>):void;onMarkMutation(index:number):void;onMoveItem(index:number,direction:-1|1):void;onPaste(event:ClipboardEvent<HTMLDivElement>):void;onRemoveItem(index:number):void;onScroll(event:UIEvent<HTMLDivElement>):void;onUpdateItemField(index:number,column:InvoiceItemColumnDefinition,value:string|number|undefined):void;};

export function InvoiceItemsTable(props:Props){
 const {activeFocusedCell,activeFocusedCellOptions,currency,displayItems,invoiceItemTableMinWidth,itemsCount,meaningfulItemCount,readOnly,selectedCellKeys,tableFrameRef,totals,virtualRowRange,visibleColumns,visibleDisplayItems}=props;
 const {onCellMouseDown:handleCellMouseDown,onDuplicateItem,onFocusCell:focusItemCell,onKeyDown:handleKeyDown,onMarkMutation:markInvoiceItemMutationFrom,onMoveItem,onPaste:handlePaste,onRemoveItem,onScroll:handleTableScroll,onUpdateItemField:updateItemField}=props;
 return <>
      <div
        className="table-frame item-editor-frame"
        ref={tableFrameRef}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        onScroll={handleTableScroll}
      >
        <table className="item-editor-table" style={{ minWidth: invoiceItemTableMinWidth }}>
          <colgroup>
            <col className="item-action-col" />
            {visibleColumns.map((column) => (
              <col key={column.field} className={column.colClassName} />
            ))}
          </colgroup>
          <thead>
            <tr>
              <th>操作</th>
              {visibleColumns.map((column) => (
                <th key={column.field} className={column.headerClassName}>
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {displayItems.length === 0 ? (
              <tr>
                <td colSpan={visibleColumns.length + 1} className="empty-cell small-empty">
                  暂无明细
                </td>
              </tr>
            ) : (
              <>
                {virtualRowRange.topSpacerHeight > 0 ? (
                  <tr className="item-virtual-spacer-row" aria-hidden="true">
                    <td
                      className="item-virtual-spacer-cell"
                      colSpan={visibleColumns.length + 1}
                      style={{ height: virtualRowRange.topSpacerHeight }}
                    />
                  </tr>
                ) : null}
                {visibleDisplayItems.map((item, rowOffset) => {
                const index = virtualRowRange.startIndex + rowOffset;
                const isPlaceholderRow = index >= itemsCount;
                return (
                <tr key={`${item.id || "new"}-${index}`} className={isPlaceholderRow ? "item-placeholder-row" : undefined}>
                  <td className="item-action-cell">
                    <div className="item-row-actions">
                      <button
                        className="icon-button compact-icon-button"
                        type="button"
                        title="复制新增明细行"
                        aria-label={`复制新增第 ${index + 1} 行明细`}
                        disabled={readOnly || isPlaceholderRow}
                        onFocus={() => focusItemCell({ rowIndex: index, field: firstEditableInvoiceItemField })}
                        onClick={() => {
                          markInvoiceItemMutationFrom(index);
                          onDuplicateItem(index);
                          focusItemCell({ rowIndex: index + 1, field: firstEditableInvoiceItemField });
                        }}
                      >
                        <Copy size={15} aria-hidden="true" />
                      </button>
                      <button
                        className="icon-button compact-icon-button"
                        type="button"
                        title="上移明细行"
                        aria-label={`上移第 ${index + 1} 行明细`}
                        disabled={readOnly || isPlaceholderRow || index === 0}
                        onFocus={() => focusItemCell({ rowIndex: index, field: firstEditableInvoiceItemField })}
                        onClick={() => {
                          markInvoiceItemMutationFrom(Math.max(0, index - 1));
                          onMoveItem(index, -1);
                          focusItemCell({ rowIndex: Math.max(0, index - 1), field: firstEditableInvoiceItemField });
                        }}
                      >
                        <ArrowUp size={15} aria-hidden="true" />
                      </button>
                      <button
                        className="icon-button compact-icon-button"
                        type="button"
                        title="下移明细行"
                        aria-label={`下移第 ${index + 1} 行明细`}
                        disabled={readOnly || isPlaceholderRow || index >= itemsCount - 1}
                        onFocus={() => focusItemCell({ rowIndex: index, field: firstEditableInvoiceItemField })}
                        onClick={() => {
                          markInvoiceItemMutationFrom(index);
                          onMoveItem(index, 1);
                          focusItemCell({ rowIndex: Math.min(itemsCount - 1, index + 1), field: firstEditableInvoiceItemField });
                        }}
                      >
                        <ArrowDown size={15} aria-hidden="true" />
                      </button>
                      <button
                        className="icon-button compact-icon-button"
                        type="button"
                        title="删除明细" aria-label="删除明细"
                        disabled={readOnly || isPlaceholderRow}
                        onFocus={() => focusItemCell({ rowIndex: index, field: firstEditableInvoiceItemField })}
                        onClick={() => {
                          markInvoiceItemMutationFrom(index);
                          onRemoveItem(index);
                        }}
                      >
                        <Trash2 size={15} aria-hidden="true" />
                      </button>
                    </div>
                  </td>
                  {visibleColumns.map((column) => {
                    const isFocusedInput = activeFocusedCell?.rowIndex === index && activeFocusedCell.field === column.field;

                    return (
                      <td key={column.field}>
                        <InvoiceItemCellInput
                          item={item}
                          index={index}
                          column={column}
                          selected={selectedCellKeys.has(createCellKey({ rowIndex: index, field: column.field }))}
                          options={isFocusedInput ? activeFocusedCellOptions : undefined}
                          disabled={readOnly}
                          onFocus={() => focusItemCell({ rowIndex: index, field: column.field })}
                          onMouseDown={(event) => handleCellMouseDown(event, { rowIndex: index, field: column.field })}
                          onChange={(value) => updateItemField(index, column, value)}
                        />
                      </td>
                    );
                  })}
                </tr>
                );
              })}
                {virtualRowRange.bottomSpacerHeight > 0 ? (
                  <tr className="item-virtual-spacer-row" aria-hidden="true">
                    <td
                      className="item-virtual-spacer-cell"
                      colSpan={visibleColumns.length + 1}
                      style={{ height: virtualRowRange.bottomSpacerHeight }}
                    />
                  </tr>
                ) : null}
              </>
            )}
          </tbody>
        </table>
      </div>
      <div className="item-summary-bar">
        <span>{meaningfulItemCount} 行</span>
        <span>数量 {formatPlainNumber(totals.totalQuantity ?? 0)}</span>
        <span>箱数 {formatPlainNumber(totals.totalCartons ?? 0)}</span>
        <span>毛重 {formatPlainNumber(totals.totalGrossWeight ?? 0)}</span>
        <span>体积 {formatPlainNumber(totals.totalVolume ?? 0)}</span>
        <span>金额 {formatAmount(totals.totalAmount ?? 0, currency)}</span>
      </div>

 </>;
}

