import type { FormEventHandler, KeyboardEventHandler, ReactNode } from "react";
import { Minimize2, PackageSearch, Save } from "lucide-react";
import type { ApiInvoiceDetailDto } from "../../api/index.ts";

type InvoiceEditorFormShellProps = {
  invoice: ApiInvoiceDetailDto;
  isWorkbench: boolean;
  isBusy: boolean;
  isEditable: boolean;
  formClassName: string;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onKeyDownCapture: KeyboardEventHandler<HTMLFormElement>;
  onCloseWorkbench: () => void;
  itemsPanel: ReactNode;
  documentSections: ReactNode;
};

/**
 * Keeps the invoice editor's form chrome independent from the domain panels.
 * The page owns mutations and policy; this component owns the two presentation
 * modes (normal editor and dense item workbench).
 */
export function InvoiceEditorFormShell({
  invoice,
  isWorkbench,
  isBusy,
  isEditable,
  formClassName,
  onSubmit,
  onKeyDownCapture,
  onCloseWorkbench,
  itemsPanel,
  documentSections,
}: InvoiceEditorFormShellProps) {
  return (
    <form className={formClassName} onSubmit={onSubmit} onKeyDownCapture={onKeyDownCapture}>
      {isWorkbench ? (
        <div className="invoice-items-focus-shell" aria-label="商品明细工作台">
          <div className="invoice-items-focus-header">
            <button className="command-button secondary" type="button" onClick={onCloseWorkbench}>
              <Minimize2 size={17} aria-hidden="true" />
              <span>返回发票</span>
            </button>
            <div className="invoice-items-focus-title">
              <PackageSearch size={18} aria-hidden="true" />
              <strong>商品明细工作台</strong>
              <span>{invoice.invoiceNo || "新建发票"}</span>
            </div>
            <button className="command-button" type="submit" disabled={isBusy || !isEditable}>
              <Save size={17} aria-hidden="true" />
              <span>保存</span>
            </button>
          </div>
          {itemsPanel}
        </div>
      ) : (
        <>{documentSections}</>
      )}
    </form>
  );
}
