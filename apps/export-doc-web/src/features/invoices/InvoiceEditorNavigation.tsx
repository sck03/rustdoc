import { CaseUpper } from "lucide-react";
import { DocumentEditorNavigation } from "../../ui/DocumentEditorTabs.tsx";
import { Button } from "../../ui/Button.tsx";
import { invoiceEditorSections, type InvoiceEditorSectionId } from "./invoiceEditorSections.ts";

export function InvoiceEditorNavigation({ invoiceNo, itemCount, totalLabel, onUppercase, ...props }: {
  invoiceNo: string; isNew: boolean; itemCount: number; totalLabel: string; activeSection: InvoiceEditorSectionId;
  editable: boolean; busy: boolean; saving: boolean; hasUnsavedChanges: boolean;
  onNavigate: (section: InvoiceEditorSectionId) => void; onUppercase: () => void;
}) {
  return <DocumentEditorNavigation {...props} prefix="invoice" label="发票" title={invoiceNo || "新建发票"}
    summary={`${itemCount} 项商品 · ${totalLabel}`} tabs={invoiceEditorSections.map((section) => ({ ...section, badge: section.id === "items" ? itemCount : undefined }))}
    action={<Button className="invoice-uppercase-action" disabled={!props.editable || props.busy} onClick={onUppercase} icon={<CaseUpper size={17} aria-hidden="true" />}>
      英文转大写
    </Button>} />;
}
