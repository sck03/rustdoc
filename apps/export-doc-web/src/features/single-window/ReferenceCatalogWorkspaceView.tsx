import type {
  ClipboardEventHandler,
  KeyboardEventHandler,
  MutableRefObject,
  MouseEventHandler,
} from "react";
import {
  ClipboardPaste,
  Download,
  FileSpreadsheet,
  Plus,
  RefreshCw,
  RotateCcw,
  Save,
  Trash2,
  Upload,
} from "lucide-react";
import type { SingleWindowReferenceCatalogModel } from "../../api/index.ts";
import {
  catalogPages,
  joinAliases,
  readAliases,
  readRowString,
  type CatalogCellPosition,
  type CatalogColumn,
  type CatalogKey,
  type CatalogPageDefinition,
  type CatalogRow,
} from "./referenceCatalogModel.ts";
import type { ReferenceCatalogExcelWorkspace } from "./useReferenceCatalogExcelWorkspace.ts";

export type CatalogContextMenuState = {
  x: number;
  y: number;
  cell: CatalogCellPosition | null;
};

export type AliasEditorState = CatalogCellPosition & {
  value: string;
};

type ToolbarProps = {
  activeKey: CatalogKey;
  draft: SingleWindowReferenceCatalogModel | null;
  rows: CatalogRow[];
  canManage: boolean;
  canReset: boolean;
  canSave: boolean;
  isBusy: boolean;
  onActiveKeyChange: (key: CatalogKey) => void;
  onRefresh: () => void;
  onExportJson: () => void;
  onChooseJsonImport: () => void;
  onChooseExcelImport: () => void;
  onAddRow: () => void;
  onPaste: () => void;
  onDeduplicate: () => void;
  onSave: () => void;
  onReset: () => void;
};

export function ReferenceCatalogToolbar({
  activeKey,
  draft,
  rows,
  canManage,
  canReset,
  canSave,
  isBusy,
  onActiveKeyChange,
  onRefresh,
  onExportJson,
  onChooseJsonImport,
  onChooseExcelImport,
  onAddRow,
  onPaste,
  onDeduplicate,
  onSave,
  onReset,
}: ToolbarProps) {
  return (
    <div className="toolbar single-window-reference-toolbar">
      <div className="reference-catalog-tabs" aria-label="参考词典分类">
        {catalogPages.map((page) => (
          <button
            key={page.key}
            className={page.key === activeKey ? "reference-catalog-tab reference-catalog-tab-active" : "reference-catalog-tab"}
            type="button"
            onClick={() => onActiveKeyChange(page.key)}
          >
            {page.label}
          </button>
        ))}
      </div>
      <div className="toolbar-actions">
        <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={onRefresh}>
          <RefreshCw size={18} aria-hidden="true" />
        </button>
        <button className="command-button secondary" type="button" disabled={!draft || isBusy} onClick={onExportJson}>
          <Download size={17} aria-hidden="true" />
          <span>导出配置</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canManage || isBusy} onClick={onChooseJsonImport}>
          <Upload size={17} aria-hidden="true" />
          <span>导入配置</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canManage || isBusy} onClick={onChooseExcelImport}>
          <FileSpreadsheet size={17} aria-hidden="true" />
          <span>Excel导入</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canManage || isBusy} onClick={onAddRow}>
          <Plus size={17} aria-hidden="true" />
          <span>新增</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canManage || isBusy} onClick={onPaste}>
          <ClipboardPaste size={17} aria-hidden="true" />
          <span>批量粘贴</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!canManage || rows.length === 0 || isBusy} onClick={onDeduplicate}>
          <RefreshCw size={17} aria-hidden="true" />
          <span>去重</span>
        </button>
        <button className="command-button" type="button" disabled={!canSave} onClick={onSave}>
          <Save size={17} aria-hidden="true" />
          <span>保存</span>
        </button>
        <button className="command-button danger-command" type="button" disabled={!canReset || isBusy} onClick={onReset}>
          <RotateCcw size={17} aria-hidden="true" />
          <span>恢复内置</span>
        </button>
      </div>
    </div>
  );
}

export function ReferenceCatalogExcelPanel({
  activePage,
  workspace,
}: {
  activePage: CatalogPageDefinition;
  workspace: ReferenceCatalogExcelWorkspace;
}) {
  if (!workspace.file && !workspace.preview) {
    return null;
  }

  return (
    <div className="reference-catalog-excel-panel" aria-label="Excel 导入">
      <div className="reference-catalog-excel-header">
        <div>
          <strong>{workspace.file?.name || "Excel 导入"}</strong>
          <span>{workspace.preview ? `${workspace.preview.sheetName} / ${workspace.preview.rowCount} 行` : activePage.label}</span>
        </div>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={!workspace.canPreview} onClick={workspace.previewFile}>
            <RefreshCw size={16} aria-hidden="true" />
            <span>预览</span>
          </button>
          <button className="command-button" type="button" disabled={!workspace.canApply} onClick={workspace.applyPreview}>
            <FileSpreadsheet size={16} aria-hidden="true" />
            <span>应用到草稿</span>
          </button>
        </div>
      </div>
      <div className="reference-catalog-excel-grid">
        <label>
          <span>工作表</span>
          <select
            value={workspace.sheetName}
            disabled={workspace.isBusy || !workspace.preview?.sheetNames?.length}
            onChange={(event) => workspace.setSheetName(event.target.value)}
          >
            {workspace.preview?.sheetNames?.length ? workspace.preview.sheetNames.map((sheetName) => (
              <option key={sheetName} value={sheetName}>{sheetName}</option>
            )) : <option value="">自动</option>}
          </select>
        </label>
        <label>
          <span>表头行</span>
          <input type="number" min={1} value={workspace.headerRowNumber} disabled={workspace.isBusy}
            onChange={(event) => workspace.setHeaderRowNumber(event.target.value)} />
        </label>
        <label>
          <span>数据起始行</span>
          <input type="number" min={1} value={workspace.dataStartRowNumber} disabled={workspace.isBusy}
            onChange={(event) => workspace.setDataStartRowNumber(event.target.value)} />
        </label>
        <label>
          <span>导入方式</span>
          <select value={workspace.importMode} disabled={workspace.isBusy}
            onChange={(event) => workspace.setImportMode(event.target.value === "replace" ? "replace" : "append")}
          >
            <option value="append">追加并去重</option>
            <option value="replace">替换当前页</option>
          </select>
        </label>
        {activePage.columns.map((column) => (
          <label key={column.key}>
            <span>{column.label}列号</span>
            <input type="number" min={0} value={workspace.columnMap[column.key] ?? ""} disabled={workspace.isBusy}
              onChange={(event) => workspace.updateColumn(column.key, event.target.value)} />
          </label>
        ))}
      </div>
    </div>
  );
}

type TableProps = {
  activePage: CatalogPageDefinition;
  rows: CatalogRow[];
  canManage: boolean;
  isBusy: boolean;
  tableFrameRef: MutableRefObject<HTMLDivElement | null>;
  onContextMenu: MouseEventHandler<HTMLDivElement>;
  onKeyDown: KeyboardEventHandler<HTMLDivElement>;
  onPaste: ClipboardEventHandler<HTMLDivElement>;
  onFocusCell: (position: CatalogCellPosition) => void;
  onUpdateRow: (rowIndex: number, column: CatalogColumn, value: string) => void;
  onDeleteRow: (rowIndex: number) => void;
};

export function ReferenceCatalogTable({
  activePage,
  rows,
  canManage,
  isBusy,
  tableFrameRef,
  onContextMenu,
  onKeyDown,
  onPaste,
  onFocusCell,
  onUpdateRow,
  onDeleteRow,
}: TableProps) {
  return (
    <div className="table-frame reference-catalog-table-frame" ref={tableFrameRef} role="region"
      aria-label="单一窗口参考目录编辑表" aria-busy={isBusy} tabIndex={0}
      onContextMenu={onContextMenu} onKeyDown={onKeyDown} onPaste={onPaste}>
      <table className="reference-catalog-table">
        <thead>
          <tr>
            {activePage.columns.map((column) => <th key={column.key}>{column.label}</th>)}
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {rows.length > 0 ? rows.map((row, index) => (
            <tr key={`${activePage.key}-${index}`}>
              {activePage.columns.map((column, columnIndex) => (
                <td key={column.key} className={column.kind === "aliases" ? "reference-catalog-alias-cell" : undefined}>
                  {column.kind === "aliases" ? (
                    <textarea data-catalog-row={index} data-catalog-column={columnIndex}
                      value={joinAliases(readAliases(row))} disabled={!canManage || isBusy}
                      aria-label={`${activePage.label} 第 ${index + 1} 行 ${column.label}`}
                      onFocus={() => onFocusCell({ rowIndex: index, columnIndex })}
                      onChange={(event) => onUpdateRow(index, column, event.target.value)} />
                  ) : (
                    <input data-catalog-row={index} data-catalog-column={columnIndex}
                      value={readRowString(row, column.key)} disabled={!canManage || isBusy}
                      aria-label={`${activePage.label} 第 ${index + 1} 行 ${column.label}`}
                      onFocus={() => onFocusCell({ rowIndex: index, columnIndex })}
                      onChange={(event) => onUpdateRow(index, column, event.target.value)} />
                  )}
                </td>
              ))}
              <td>
                <button className="icon-button danger-icon" type="button" title="删除" aria-label="删除"
                  disabled={!canManage || isBusy} onClick={() => onDeleteRow(index)}>
                  <Trash2 size={17} aria-hidden="true" />
                </button>
              </td>
            </tr>
          )) : (
            <tr><td className="empty-cell" colSpan={activePage.columns.length + 1}>{isBusy ? "加载中" : "暂无词典行"}</td></tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

export function ReferenceCatalogContextMenu({
  contextMenu,
  activePage,
  rows,
  canManage,
  isBusy,
  onAddRow,
  onDeleteRow,
  onPaste,
  onDeduplicate,
  onOpenAliasEditor,
}: {
  contextMenu: CatalogContextMenuState;
  activePage: CatalogPageDefinition;
  rows: CatalogRow[];
  canManage: boolean;
  isBusy: boolean;
  onAddRow: () => void;
  onDeleteRow: () => void;
  onPaste: () => void;
  onDeduplicate: () => void;
  onOpenAliasEditor: () => void;
}) {
  return (
    <div className="reference-catalog-context-menu" role="menu" style={{ left: contextMenu.x, top: contextMenu.y }}
      onClick={(event) => event.stopPropagation()} onContextMenu={(event) => event.preventDefault()}
      onMouseDown={(event) => event.stopPropagation()}>
      <button type="button" role="menuitem" disabled={!canManage || isBusy} onClick={onAddRow}>新增一行</button>
      <button type="button" role="menuitem" disabled={!canManage || isBusy || !contextMenu.cell} onClick={onDeleteRow}>删除当前行</button>
      <button type="button" role="menuitem" disabled={!canManage || isBusy} onClick={onPaste}>批量粘贴</button>
      <button type="button" role="menuitem" disabled={!canManage || isBusy || rows.length === 0} onClick={onDeduplicate}>批量去重</button>
      <button type="button" role="menuitem"
        disabled={!canManage || isBusy || activePage.columns[contextMenu.cell?.columnIndex ?? -1]?.kind !== "aliases"}
        onClick={onOpenAliasEditor}>编辑别名...</button>
    </div>
  );
}

export function ReferenceCatalogAliasDialog({
  activePage,
  editor,
  dialogRef,
  inputRef,
  onClose,
  onChange,
  onApply,
}: {
  activePage: CatalogPageDefinition;
  editor: AliasEditorState;
  dialogRef: MutableRefObject<HTMLDivElement | null>;
  inputRef: MutableRefObject<HTMLTextAreaElement | null>;
  onClose: () => void;
  onChange: (value: string) => void;
  onApply: () => void;
}) {
  return (
    <div className="workspace-modal-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose();
    }}>
      <div ref={dialogRef} className="workspace-modal-dialog reference-catalog-alias-dialog"
        role="dialog" aria-modal="true" aria-labelledby="reference-catalog-alias-title">
        <div className="workspace-modal-header">
          <div className="workspace-modal-title">
            <h2 id="reference-catalog-alias-title">编辑别名</h2>
            <span>{activePage.label}</span>
          </div>
        </div>
        <div className="workspace-modal-toolbar"><span>第 {editor.rowIndex + 1} 行</span></div>
        <textarea ref={inputRef} value={editor.value} onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              onClose();
            }
            if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
              event.preventDefault();
              onApply();
            }
          }} />
        <div className="workspace-modal-footer">
          <button className="command-button secondary" type="button" onClick={onClose}>取消</button>
          <button className="command-button" type="button" onClick={onApply}>应用</button>
        </div>
      </div>
    </div>
  );
}
