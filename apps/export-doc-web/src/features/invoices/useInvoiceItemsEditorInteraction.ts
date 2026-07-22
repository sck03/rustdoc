import { useState } from "react";
import type { ApiProductDto } from "../../api/index.ts";
import type { UnitCandidateDialogState } from "./InvoiceItemsAssist.tsx";
import type { EditableInvoiceItemField } from "./invoiceItemTableModel.ts";

export function useInvoiceItemsEditorInteraction() {
  const [unitCandidateDialog, setUnitCandidateDialog] = useState<UnitCandidateDialogState | null>(null);
  const [isProductPickerOpen, setIsProductPickerOpen] = useState(false);
  const [isHsKnowledgeOpen, setIsHsKnowledgeOpen] = useState(false);
  const [productKeyword, setProductKeyword] = useState("");
  const [selectedProductId, setSelectedProductId] = useState("");
  const [hiddenColumnFields, setHiddenColumnFields] = useState<Set<EditableInvoiceItemField>>(new Set());
  const openProductPicker = () => setIsProductPickerOpen(true);
  const closeProductPicker = () => setIsProductPickerOpen(false);
  const openHsKnowledge = () => setIsHsKnowledgeOpen(true);
  const closeHsKnowledge = () => setIsHsKnowledgeOpen(false);
  const selectProduct = (product: ApiProductDto) => setSelectedProductId(String(product.id));
  return { unitCandidateDialog, setUnitCandidateDialog, isProductPickerOpen, setIsProductPickerOpen, isHsKnowledgeOpen, setIsHsKnowledgeOpen, productKeyword, setProductKeyword, selectedProductId, setSelectedProductId, hiddenColumnFields, setHiddenColumnFields, openProductPicker, closeProductPicker, openHsKnowledge, closeHsKnowledge, selectProduct };
}
