import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";

const PreviewContext = createContext<{
  mode: "products" | "entries";
  setMode: (mode: "products" | "entries") => void;
  column: { blockId: string; columnId: string } | null;
  selectColumn: (blockId: string, columnId: string) => void;
  labels: ReadonlyMap<string, string>;
}>({ mode: "products", setMode: () => {}, column: null, selectColumn: () => {}, labels: new Map() });

/** Canvas inspection state never becomes part of the printable document or undo history. */
export function ReportDesignerPreviewProvider({ fieldGroups, children }: { fieldGroups: ReportDesignerFieldGroup[]; children: ReactNode }) {
  const [mode, setMode] = useState<"products" | "entries">("products");
  const [column, setColumn] = useState<{ blockId: string; columnId: string } | null>(null);
  const labels = useMemo(() => new Map(fieldGroups.flatMap(group => group.fields).map(field => [field.value, field.label])), [fieldGroups]);
  const value = useMemo(() => ({ mode, setMode, column, labels,
    selectColumn: (blockId: string, columnId: string) => setColumn(current => current?.blockId === blockId && current.columnId === columnId ? current : { blockId, columnId }),
  }), [mode, column, labels]);
  return <PreviewContext value={value}>{children}</PreviewContext>;
}

export const useReportDesignerPreview = () => useContext(PreviewContext);
