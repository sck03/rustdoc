import { Code2, Columns3, FileCheck2, FilePlus2, Grid2X2, Image as ImageIcon, ListFilter, Pilcrow, Redo2, RotateCcw, Save, Table2, Undo2 } from "lucide-react";

export function ReportDesignerToolbar({
  canUndo,
  canRedo,
  canApply,
  canSave,
  onBack,
  onReset,
  onApply,
  onSave,
  onInsertText,
  onInsertRow,
  onInsertGrid,
  onInsertConditional,
  onInsertImage,
  onInsertDetailTable,
  onInsertPageBreak,
  canInsertDetailTable,
  onUndo,
  onRedo,
}: {
  canUndo: boolean;
  canRedo: boolean;
  canApply: boolean;
  canSave: boolean;
  onBack: () => void;
  onReset: () => void;
  onApply: () => void;
  onSave: () => void;
  onInsertText: () => void;
  onInsertRow: () => void;
  onInsertGrid: () => void;
  onInsertConditional: () => void;
  onInsertImage: () => void;
  onInsertDetailTable: () => void;
  onInsertPageBreak: () => void;
  canInsertDetailTable: boolean;
  onUndo: () => void;
  onRedo: () => void;
}) {
  return (
    <div className="new-report-designer-toolbar">
      <button className="command-button secondary" type="button" onClick={onBack}>
        <Code2 size={17} aria-hidden="true" />
        <span>源码</span>
      </button>
      <div className="new-report-designer-toolbar-actions">
        <button className="command-button secondary" type="button" disabled={!canApply} onClick={onApply}>
          <FileCheck2 size={17} aria-hidden="true" />
          <span>应用到源码</span>
        </button>
        <button className="command-button" type="button" disabled={!canSave} onClick={onSave}>
          <Save size={17} aria-hidden="true" />
          <span>保存模板</span>
        </button>
        <button className="command-button secondary" type="button" onClick={onInsertText}>
          <Pilcrow size={17} aria-hidden="true" />
          <span>文本</span>
        </button>
        <button className="command-button secondary" type="button" onClick={onInsertRow}>
          <Columns3 size={17} aria-hidden="true" />
          <span>行</span>
        </button>
        <button className="command-button secondary" type="button" onClick={onInsertGrid}>
          <Grid2X2 size={17} aria-hidden="true" />
          <span>票据格</span>
        </button>
        <button className="command-button secondary" type="button" onClick={onInsertConditional}>
          <ListFilter size={17} aria-hidden="true" />
          <span>条件</span>
        </button>
        <button className="command-button secondary" type="button" onClick={onInsertImage}>
          <ImageIcon size={17} aria-hidden="true" />
          <span>图片/印章</span>
        </button>
        {canInsertDetailTable ? (
          <button className="command-button secondary" type="button" onClick={onInsertDetailTable}>
            <Table2 size={17} aria-hidden="true" />
            <span>明细表</span>
          </button>
        ) : null}
        <button className="command-button secondary" type="button" onClick={onInsertPageBreak}>
          <FilePlus2 size={17} aria-hidden="true" />
          <span>分页符</span>
        </button>
        <button className="icon-button" type="button" title="撤销" aria-label="撤销" disabled={!canUndo} onClick={onUndo}>
          <Undo2 size={18} aria-hidden="true" />
        </button>
        <button className="icon-button" type="button" title="重做" aria-label="重做" disabled={!canRedo} onClick={onRedo}>
          <Redo2 size={18} aria-hidden="true" />
        </button>
        <button className="command-button secondary" type="button" onClick={onReset}>
          <RotateCcw size={17} aria-hidden="true" />
          <span>重置结构</span>
        </button>
      </div>
    </div>
  );
}
