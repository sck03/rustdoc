import type { ReactNode } from "react";
import { DocumentEditorTabPanel } from "../../ui/DocumentEditorTabs.tsx";
import type { InvoiceEditorSectionId } from "./invoiceEditorSections.ts";

export function InvoiceEditorSection(props: { id: InvoiceEditorSectionId; activeSection: InvoiceEditorSectionId; children: ReactNode }) {
  return <DocumentEditorTabPanel {...props} prefix="invoice" eager={props.id === "header"} />;
}
