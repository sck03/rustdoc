import { useEffect, useState } from "react";
import type { ReportGridBlock } from "./reportDesignerSchema.ts";
import { formatGridCellAddress, type ReportGridCellLocation } from "./reportDesignerGridStructure.ts";
import { importGridText, MAX_GRID_PASTE_CHARACTERS } from "./reportDesignerGridTextImport.ts";

export function GridTextImport({ block, selected, onCommit }: {
  block: ReportGridBlock; selected?: ReportGridCellLocation; onCommit: (block: ReportGridBlock) => void;
}) {
  const [text, setText] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  useEffect(() => { setText(""); setNotice(null); }, [block.id]);
  return <details className="report-designer-property-section">
    <summary>从表格粘贴文字</summary>
    <label className="new-report-property-wide"><span>表格文字</span><textarea rows={5} maxLength={MAX_GRID_PASTE_CHARACTERS}
      value={text} placeholder="从 Excel 或其他表格复制单元格后粘贴到这里" onChange={event => { setText(event.target.value); setNotice(null); }} /></label>
    <small>从 {selected ? formatGridCellAddress(selected.rowIndex, selected.columnIndex) : "当前格"} 向右、向下填入，需要时扩展表格；保留样式，可一步撤销。已有字段和勾选组不会被覆盖。</small>
    {notice && <small role="alert">{notice}</small>}
    <button type="button" className="command-button secondary" disabled={!selected || !text} onClick={() => {
      if (!selected) return;
      const result = importGridText(block, selected.cell.id, text);
      setNotice(result.notice);
      if (!result.notice) { onCommit(result.block); setText(""); }
    }}>填入表格</button>
  </details>;
}
