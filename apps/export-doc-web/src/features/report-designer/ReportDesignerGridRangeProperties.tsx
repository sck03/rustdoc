import { TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { mergeGridSelection, splitGridSelection } from "./reportDesignerGridStructure.ts";
import { patchGridCellStyles, setGridSelectionBorders } from "./reportDesignerGridMutations.ts";
import type { ResolvedGridSelection } from "./reportDesignerGridSelection.ts";
import type { ReportGridBlock } from "./reportDesignerSchema.ts";

export function GridRangeProperties({ block, selected, onCommit, onNotice }: {
  block: ReportGridBlock;
  selected: ResolvedGridSelection;
  onCommit: (block: ReportGridBlock) => void;
  onNotice: (notice: string | null) => void;
}) {
  const selection = { cellId: selected.anchor.cell.id, endCellId: selected.end.cell.id };
  const ids = new Set(selected.cells.map(location => location.cell.id));
  return <section className="new-report-grid-cell-editor" aria-label="所选单元格区域">
    <strong>已选择 {selected.cells.length} 个单元格</strong>
    <div className="new-report-grid-structure-actions">
      <button type="button" className="command-button secondary" onClick={() => {
        const result = mergeGridSelection(block, selection);
        onNotice(result.notice);
        onCommit(result.block);
      }}>合并所选区域</button>
      <button type="button" className="command-button secondary" disabled={!selected.cells.some(cell => cell.rowSpan > 1 || cell.colSpan > 1)}
        onClick={() => onCommit(splitGridSelection(block, selection))}>拆分所选区域</button>
    </div>
    <TextStyleEditor style={{ ...block.defaultCellStyle, ...selected.anchor.cell.style }}
      onChange={(_, patch) => onCommit(patchGridCellStyles(block, ids, patch))} />
    <div className="new-report-grid-structure-actions" role="group" aria-label="所选区域边框">
      {([['all', '全部框线'], ['outline', '仅外框'], ['none', '清除框线']] as const).map(([preset, label]) =>
        <button key={preset} type="button" className="command-button secondary" onClick={() => onCommit(setGridSelectionBorders(block, selected, preset))}>{label}</button>)}
    </div>
  </section>;
}
