import { useEffect, useState, type ReactNode } from "react";
import type { InvoiceEditorSectionId } from "./invoiceEditorSections.ts";

export function InvoiceEditorSection({ id, activeSection, children }: {
  id: InvoiceEditorSectionId;
  activeSection: InvoiceEditorSectionId;
  children: ReactNode;
}) {
  const active = id === activeSection;
  const [visited, setVisited] = useState(active || id === "header");
  useEffect(() => { if (active) setVisited(true); }, [active]);
  return <div id={`invoice-${id}-section`} data-invoice-section={id} className="invoice-editor-section-anchor invoice-editor-tab-panel"
    role="tabpanel" aria-labelledby={`invoice-tab-${id}`} hidden={!active} tabIndex={0}>
    {active || visited ? children : null}
  </div>;
}
