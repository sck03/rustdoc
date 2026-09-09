import { useEffect, useState } from "react";
import { applySelectedV3ElementStyle, findV3Element, getV3ElementCapacityIssue, pasteV3Elements, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import type { ReportDesignerV3Element } from "./reportDesignerV3Schema.ts";

export function useReportDesignerV3Clipboard({ state, editable, content, reportType, onCommit, onNotice }: {
  state: ReportDesignerV3DocumentState; editable: boolean; content: string; reportType: string;
  onCommit: (state: ReportDesignerV3DocumentState) => void; onNotice: (message: string | null) => void;
}) {
  const [elements, setElements] = useState<ReportDesignerV3Element[]>([]);
  const [style, setStyle] = useState<ReportDesignerV3Element["style"] | null>(null);
  useEffect(() => { setElements([]); setStyle(null); }, [content, reportType]);
  const selected = state.selectedIds.map((id) => findV3Element(state.schema, id)).filter((item) => item !== null);
  const source = selected.length === 1 && selected[0].element.type !== "Flow" ? selected[0].element : null;
  const canPasteStyle = editable && style !== null && selected.some(({ element, layer }) => element.type !== "Flow" && !element.locked && !layer.locked);
  return {
    hasClipboard: elements.length > 0,
    canCopyStyle: source !== null,
    canPasteStyle,
    copySelection() { if (selected.length) setElements(selected.map((item) => item.element)); },
    pasteClipboard() {
      if (!editable || elements.length === 0) return;
      const next = pasteV3Elements(state, elements, state.activeLayerId ?? undefined);
      onNotice(next === state ? getV3ElementCapacityIssue(state, state.activeLayerId ?? undefined, elements.length) ?? "当前区域已锁定、隐藏或无法容纳这些组件。" : null);
      onCommit(next);
    },
    copyStyle() { if (source) setStyle({ ...source.style }); },
    pasteStyle() { if (canPasteStyle && style) onCommit(applySelectedV3ElementStyle(state, style)); },
  };
}
