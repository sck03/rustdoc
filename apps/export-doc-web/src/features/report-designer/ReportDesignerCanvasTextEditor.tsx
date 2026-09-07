import { useEffect, useRef } from "react";

export type ReportDesignerTextEdit = {
  elementId: string;
  cellId?: string;
  text: string;
  left: number;
  top: number;
  width: number;
  height: number;
};

export function ReportDesignerCanvasTextEditor({ edit, zoom, onCommit, onCancel }: {
  edit: ReportDesignerTextEdit;
  zoom: number;
  onCommit: (value: string) => void;
  onCancel: () => void;
}) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const settled = useRef(false);
  useEffect(() => { ref.current?.focus(); ref.current?.select(); }, []);
  function finish(value: string, cancel = false) {
    if (settled.current) return;
    settled.current = true;
    if (cancel) onCancel();
    else onCommit(value);
  }
  return <div className="report-designer-canvas-text-editor" style={{ left: edit.left, top: edit.top, width: edit.width, height: edit.height }} onPointerDown={(event) => event.stopPropagation()}>
    <textarea ref={ref} aria-label={edit.cellId ? "在画布编辑单元格文字" : "在画布编辑文字"} title="Ctrl+Enter 确认，Esc 取消；点击画布其他位置也可确认" defaultValue={edit.text} style={{ fontSize: `${12 / zoom}px` }} onBlur={(event) => finish(event.currentTarget.value)} onKeyDown={(event) => {
      if (event.nativeEvent.isComposing) return;
      event.stopPropagation();
      if (event.key === "Escape") { event.preventDefault(); finish(event.currentTarget.value, true); }
      if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) { event.preventDefault(); finish(event.currentTarget.value); }
    }} />
  </div>;
}
