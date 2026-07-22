import { CaseUpper, ClipboardList, FileText, PackageSearch, Save, ScrollText } from "lucide-react";
import type { ReactNode } from "react";
import { Button } from "../../ui/Button.tsx";

export function InvoiceEditorNavigation({
  invoiceNo,
  editable,
  busy,
  saving,
  hasUnsavedChanges,
  onNavigate,
  onUppercase,
}: {
  invoiceNo: string;
  editable: boolean;
  busy: boolean;
  saving: boolean;
  hasUnsavedChanges: boolean;
  onNavigate: (sectionId: string) => void;
  onUppercase: () => void;
}) {
  return <>
    <nav className="invoice-editor-section-nav" aria-label="发票编辑分区">
      <SectionButton icon={<FileText size={16} aria-hidden="true" />} label="发票表头" onClick={() => onNavigate("invoice-header-section")} />
      <SectionButton primary icon={<PackageSearch size={16} aria-hidden="true" />} label="商品明细" onClick={() => onNavigate("invoice-items-section")} />
      <SectionButton icon={<ClipboardList size={16} aria-hidden="true" />} label="利润/信用证" onClick={() => onNavigate("invoice-analysis-section")} />
      <SectionButton icon={<ScrollText size={16} aria-hidden="true" />} label="预览导出" onClick={() => onNavigate("invoice-report-section")} />
      <button type="button" className="invoice-section-nav-item invoice-uppercase-action" disabled={!editable} onClick={onUppercase}>
        <CaseUpper size={17} aria-hidden="true" />
        <span>英文转大写</span>
      </button>
    </nav>
    <div className="invoice-editor-sticky-actions" role="region" aria-label="发票保存操作">
      <div>
        <strong>{invoiceNo || "新建发票"}</strong>
        <span>{hasUnsavedChanges ? "当前有未保存修改" : "当前内容已保存"}</span>
      </div>
      <Button variant="primary" type="submit" disabled={busy || !editable} icon={<Save size={17} aria-hidden="true" />}>
        {saving ? "保存中" : "保存发票"}
      </Button>
    </div>
  </>;
}

function SectionButton({ icon, label, primary = false, onClick }: { icon: ReactNode; label: string; primary?: boolean; onClick: () => void }) {
  return <button type="button" className={`invoice-section-nav-item${primary ? " invoice-section-nav-primary" : ""}`} onClick={onClick}>
    {icon}
    <span>{label}</span>
  </button>;
}
