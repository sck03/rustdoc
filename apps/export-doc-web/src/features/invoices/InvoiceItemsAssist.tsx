import { X } from "lucide-react";
import type { ApiProductDto } from "../../api/index.ts";
import { ProductLibraryPickerDialog } from "./InvoiceProductLibraryPickerDialog.tsx";

type UnitLookupSourceField="unitEN"|"ctnUnitEN"; type UnitLookupTargetField="unitCN"|"ctnUnitCN";
export type UnitCandidateDialogState={sourceField:UnitLookupSourceField;targetField:UnitLookupTargetField;targetLabel:string;rowIndex:number;unitEn:string;unitEnKey:string;candidates:string[]};
type Props={focusedRowIndex:number|null;isBusy:boolean;isProductPickerOpen:boolean;itemsCount:number;productKeyword:string;products:ApiProductDto[];readOnly:boolean;unitCandidateDialog:UnitCandidateDialogState|null;onApplyProduct(product:ApiProductDto):void;onApplyUnitCandidate(candidate:string):void;onCloseProductPicker():void;onCloseUnitCandidates():void;onRefresh():void;onSearch(keyword:string):void};
export function InvoiceItemsAssist(props:Props){const {focusedRowIndex,isBusy,isProductPickerOpen,itemsCount,productKeyword,products,readOnly,unitCandidateDialog,onApplyProduct,onApplyUnitCandidate:applyUnitCandidate,onCloseProductPicker,onCloseUnitCandidates,onRefresh,onSearch}=props;return <>
      <div className="item-editor-assist-area">
        {unitCandidateDialog ? (
          <div className="item-unit-candidate-panel" role="dialog" aria-label="选择中文单位">
            <div className="item-unit-candidate-title">
              <span>第 {unitCandidateDialog.rowIndex + 1} 行</span>
              <strong>{unitCandidateDialog.unitEn}</strong>
              <span>{unitCandidateDialog.targetLabel}</span>
            </div>
            <div className="item-unit-candidate-options">
              {unitCandidateDialog.candidates.map((candidate) => (
                <button
                  className="text-button compact-text-button"
                  key={candidate}
                  type="button"
                  disabled={readOnly}
                  onClick={() => applyUnitCandidate(candidate)}
                >
                  {candidate}
                </button>
              ))}
            </div>
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="关闭"
              aria-label="关闭中文单位候选"
              onClick={onCloseUnitCandidates}
            >
              <X size={15} aria-hidden="true" />
            </button>
          </div>
        ) : null}
      </div>
      {isProductPickerOpen ? (
        <ProductLibraryPickerDialog
          focusedRowIndex={focusedRowIndex}
          initialKeyword={productKeyword}
          isBusy={isBusy}
          itemsCount={itemsCount}
          products={products}
          readOnly={readOnly}
          onApply={onApplyProduct}
          onClose={onCloseProductPicker}
          onRefresh={onRefresh}
          onSearch={onSearch}
        />
      ) : null}

</>}
