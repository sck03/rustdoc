import { CaseUpper, Save } from "lucide-react";
import type { KeyboardEvent } from "react";
import { Button } from "../../ui/Button.tsx";
import { invoiceEditorSections, type InvoiceEditorSectionId } from "./invoiceEditorSections.ts";
import "../../styles/business/invoice-editor-navigation.css";

export function InvoiceEditorNavigation({
  invoiceNo,
  isNew,
  itemCount,
  totalLabel,
  activeSection,
  editable,
  busy,
  saving,
  hasUnsavedChanges,
  onNavigate,
  onUppercase,
}: {
  invoiceNo: string;
  isNew: boolean;
  itemCount: number;
  totalLabel: string;
  activeSection: InvoiceEditorSectionId;
  editable: boolean;
  busy: boolean;
  saving: boolean;
  hasUnsavedChanges: boolean;
  onNavigate: (section: InvoiceEditorSectionId) => void;
  onUppercase: () => void;
}) {
  function moveTab(event: KeyboardEvent<HTMLDivElement>) {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    const tabs = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    const current = tabs.indexOf(event.target as HTMLButtonElement);
    if (current < 0) return;
    event.preventDefault();
    const index = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowLeft" ? -1 : 1) + tabs.length) % tabs.length;
    onNavigate(invoiceEditorSections[index].id);
    tabs[index].focus();
  }

  return <div id="invoice-editor-navigation" className="invoice-editor-navigation">
    <div className="invoice-editor-sticky-actions" role="region" aria-label="发票保存操作">
      <div>
        <strong>{invoiceNo || "新建发票"}</strong>
        <span>{itemCount} 项商品 · {totalLabel} · {saving ? "保存中…" : hasUnsavedChanges ? "有未保存修改" : isNew ? "尚未保存" : "已保存"}</span>
      </div>
      <Button variant="primary" type="submit" disabled={busy || !editable} icon={<Save size={17} aria-hidden="true" />}>
        {saving ? "保存中" : "保存发票"}
      </Button>
    </div>
    <div className="invoice-editor-section-nav">
      <div className="invoice-editor-tabs" role="tablist" aria-label="发票编辑分区" onKeyDown={moveTab}>
        {invoiceEditorSections.map((section) => <button key={section.id} type="button" role="tab"
          id={`invoice-tab-${section.id}`} aria-controls={`invoice-${section.id}-section`} aria-selected={activeSection === section.id}
          tabIndex={activeSection === section.id ? 0 : -1} className="invoice-section-nav-item" onClick={() => onNavigate(section.id)}>
          <span>{section.label}</span>{section.id === "items" && <small>{itemCount}</small>}
        </button>)}
      </div>
      <button type="button" className="invoice-section-nav-item invoice-uppercase-action" disabled={!editable || busy} onClick={onUppercase}>
        <CaseUpper size={17} aria-hidden="true" /><span>英文转大写</span>
      </button>
    </div>
    <p className="invoice-editor-section-help">{invoiceEditorSections.find((section) => section.id === activeSection)?.description}</p>
  </div>;
}
