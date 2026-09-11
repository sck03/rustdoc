import { createContext, useContext, type ReactNode } from "react";
import type { DocumentFieldLabelSettings } from "../api/index.ts";
import { documentSpareKeys, type DocumentSpareKey } from "./documentSpareFields.ts";

const emptyLabels: DocumentFieldLabelSettings = { invoice: {}, item: {}, payment: {} };
const DocumentFieldLabelsContext = createContext(emptyLabels);

export function DocumentFieldLabelsProvider({ value, children }: { value?: DocumentFieldLabelSettings; children: ReactNode }) {
  return <DocumentFieldLabelsContext value={value ?? emptyLabels}>{children}</DocumentFieldLabelsContext>;
}

export function useDocumentFieldLabels(group: keyof DocumentFieldLabelSettings) {
  return useContext(DocumentFieldLabelsContext)[group] ?? {};
}

export function documentFieldLabel(labels: Record<string, unknown>, key: string, fallback?: string) {
  const index = documentSpareKeys.indexOf(key as DocumentSpareKey);
  const label = labels[key];
  return index < 0 ? fallback ?? key : (typeof label === "string" && label.trim()) || `备用 ${index + 1}`;
}
