import { useEffect, useMemo, useRef, useState } from "react";
import {
  AlignHorizontalJustifyCenter,
  AlignHorizontalJustifyEnd,
  AlignHorizontalJustifyStart,
  AlignVerticalJustifyCenter,
  AlignVerticalJustifyEnd,
  AlignVerticalJustifyStart,
  ArrowDown,
  ArrowUp,
  ArrowLeftRight,
  ArrowUpDown,
  Copy,
  Eye,
  EyeOff,
  FilePlus2,
  Grid2X2,
  Image as ImageIcon,
  Layers3,
  ListFilter,
  Lock,
  Minus,
  Pilcrow,
  Plus,
  Redo2,
  RotateCcw,
  Table2,
  Trash2,
  Undo2,
  Unlock,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import type { ApiReportTemplateFieldCatalogResponse } from "../../api/index.ts";
import { buildReportDesignerFieldGroups, type ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { useReportDesignerV3History } from "./reportDesignerV3History.ts";
import {
  exportReportDesignerV3SchemaToHtml,
  validateReportDesignerV3Export,
} from "./reportDesignerV3HtmlExporter.ts";
import {
  createV3FieldElement,
  createV3FlowElement,
  createV3ImageElement,
  createV3LineElement,
  createV3RectangleElement,
  createV3TextElement,
  alignSelectedV3Elements,
  deleteSelectedV3Elements,
  duplicateSelectedV3Elements,
  distributeSelectedV3Elements,
  getV3ElementCapacityIssue,
  findV3Element,
  insertV3Element,
  moveSelectedV3Elements,
  resizeV3Element,
  setV3ElementZIndex,
  toggleV3Selection,
  updateV3Element,
  updateV3Grid,
  updateV3Layer,
  updateV3Page,
  type ReportDesignerV3DocumentState,
} from "./reportDesignerV3Mutations.ts";
import { parseReportDesignerV3FromHtml } from "./reportDesignerV3TemplateParser.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  reportDesignerV3PageSize,
  type ReportDesignerV3Element,
  type ReportDesignerV3Layer,
} from "./reportDesignerV3Schema.ts";
import { ReportDesignerV3Canvas, type ReportDesignerV3Transform } from "./ReportDesignerV3Canvas.tsx";
import type { ReportBlock, ReportDesignerReportType } from "./reportDesignerSchema.ts";
import {
  createConditionalBlock,
  createDetailTableBlock,
  createGridBlock,
  createPageBreakBlock,
  createRowBlock,
} from "./reportDesignerBlockFactories.ts";
import { ReportDesignerV3FlowProperties } from "./ReportDesignerV3FlowProperties.tsx";
import {
  getControlledReportImageFieldPaths,
  isControlledReportImageFieldPath,
} from "./reportDesignerSchemaDomains.ts";
import {
  countElements,
  filterFieldGroups,
  flattenFields,
  formatNumber,
  isEditableTarget,
  migrationNoticeDescription,
  migrationNoticeTitle,
} from "./reportDesignerV3WorkspaceHelpers.tsx";

type V3SidebarTab = "components" | "fields" | "layers";

export function ReportDesignerV3Workspace({
  reportType,
  displayName,
  content,
  fieldCatalog,
  onDesignerDraftContentChange,
}: {
  reportType: ReportDesignerReportType;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null;
  onDesignerDraftContentChange?: (nextContent: string) => void;
}) {
  const parsed = useMemo(() => parseReportDesignerV3FromHtml(content, reportType), [content, reportType]);
  const history = useReportDesignerV3History(parsed.schema);
  const fieldGroups = useMemo(() => buildReportDesignerFieldGroups(fieldCatalog, reportType), [fieldCatalog, reportType]);
  const [sidebarTab, setSidebarTab] = useState<V3SidebarTab>("components");
  const [zoom, setZoom] = useState(0.72);
  const [fieldQuery, setFieldQuery] = useState("");
  const [capacityNotice, setCapacityNotice] = useState<string | null>(null);
  // Converting advanced HTML (or a damaged/removed V2 structure) is explicit;
  // opening a template must never create a dirty V3 draft.
  const legacyMigrationPending = parsed.migrated;
  const [migrationAccepted, setMigrationAccepted] = useState(!legacyMigrationPending);
  const [draftEnabled, setDraftEnabled] = useState(false);
  const emittedContent = useRef("");
  const historyRef = useRef(history);
  const editingEnabledRef = useRef(false);
  const commitRef = useRef<(next: ReportDesignerV3DocumentState, options?: { coalesce?: boolean }) => void>(() => undefined);
  const clearSelectionRef = useRef<() => void>(() => undefined);
  const duplicateSelectionRef = useRef<() => void>(() => undefined);
  const selected = useMemo(
    () => history.state.selectedIds.length === 1 ? findV3Element(history.state.schema, history.state.selectedIds[0]) : null,
    [history.state],
  );
  const exportValidation = useMemo(
    () => validateReportDesignerV3Export(history.state.schema, reportType),
    [history.state.schema, reportType],
  );
  const exportedHtml = useMemo(
    () => exportValidation.blocked ? "" : exportReportDesignerV3SchemaToHtml(history.state.schema, reportType),
    [exportValidation.blocked, history.state.schema, reportType],
  );
  const pageSize = reportDesignerV3PageSize(history.state.schema.page);
  const visibleFieldGroups = useMemo(() => filterFieldGroups(fieldGroups, fieldQuery), [fieldGroups, fieldQuery]);
  useEffect(() => {
    setMigrationAccepted(!legacyMigrationPending);
    setDraftEnabled(false);
    emittedContent.current = "";
    onDesignerDraftContentChange?.("");
  }, [legacyMigrationPending, content, reportType, onDesignerDraftContentChange]);
  useEffect(() => {
    if (!migrationAccepted || !draftEnabled) return;
    if (exportValidation.blocked || !exportedHtml.trim()) {
      // Never publish an empty string as a usable draft.  Clearing the last
      // emitted value keeps the original `content` intact and disables the
      // parent save action until the user repairs the schema.
      if (emittedContent.current) {
        emittedContent.current = "";
        onDesignerDraftContentChange?.("");
      }
      return;
    }
    if (emittedContent.current === exportedHtml) return;
    emittedContent.current = exportedHtml;
    onDesignerDraftContentChange?.(exportedHtml);
  }, [draftEnabled, exportValidation.blocked, exportedHtml, migrationAccepted, onDesignerDraftContentChange]);
  const editingEnabled = !legacyMigrationPending || migrationAccepted;
  function enableDraftEditing() {
    setMigrationAccepted(true);
    setDraftEnabled(true);
  }

  function commit(next: ReportDesignerV3DocumentState, options?: { coalesce?: boolean }) {
    if (next.schema !== history.state.schema) {
      if (!editingEnabled) return;
      setDraftEnabled(true);
      history.commit(next, options);
      return;
    }
    if (next.selectedIds.join("\u0000") !== history.state.selectedIds.join("\u0000") || next.activeLayerId !== history.state.activeLayerId) {
      history.select(next.selectedIds, next.activeLayerId);
    }
  }

  function selectElement(elementId: string, additive: boolean) {
    const next = toggleV3Selection(history.state, elementId, additive);
    history.select(next.selectedIds, next.activeLayerId);
  }

  function clearSelection() {
    history.select([], history.state.activeLayerId);
  }

  function activeLayerId() {
    const active = history.state.schema.layers.find((layer) => layer.id === history.state.activeLayerId && layer.visible && !layer.locked);
    return active?.id ?? history.state.schema.layers.find((layer) => layer.visible && !layer.locked)?.id ?? history.state.schema.layers[0]?.id ?? null;
  }

  function placeElement(element: ReportDesignerV3Element) {
    if (!editingEnabled) return;
    const anchor = selected?.element;
    const x = anchor ? anchor.xHundredthMm + Math.min(anchor.widthHundredthMm + 500, 1000) : element.xHundredthMm;
    const y = anchor ? anchor.yHundredthMm + Math.min(anchor.heightHundredthMm + 500, 1000) : element.yHundredthMm;
    const layerId = activeLayerId();
    if (!layerId) return;
    const next = insertV3Element(history.state, layerId, { ...element, xHundredthMm: x, yHundredthMm: y });
    if (next === history.state) {
      setCapacityNotice(getV3ElementCapacityIssue(history.state, layerId) ?? "当前图层不可编辑。");
      return;
    }
    setCapacityNotice(null);
    commit(next);
  }

  function insertFlow(block: ReportBlock) {
    if (block.type !== "Row" && block.type !== "Grid" && block.type !== "Conditional" && block.type !== "DetailTable" && block.type !== "PageBreak") return;
    placeElement(createV3FlowElement(block));
  }

  function insertField(field: { label: string; value: string }) {
    if (!field.value.trim()) return;
    placeElement(createV3FieldElement(field.value));
  }

  function patchSelected(update: Partial<ReportDesignerV3Element>) {
    if (!editingEnabled) return;
    if (!selected || selected.element.locked || selected.layer.locked) return;
    commit(updateV3Element(history.state, selected.element.id, update));
  }

  function patchSelectedStyle(update: Partial<ReportDesignerV3Element["style"]>) {
    if (!selected || selected.element.locked || selected.layer.locked) return;
    patchSelected({ style: { ...selected.element.style, ...update } });
  }

  function patchSelectedFlow(block: Extract<ReportBlock, { type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak" }>) {
    if (!selected || selected.element.type !== "Flow" || selected.element.flowKind !== block.type || !editingEnabled) return;
    const next = updateV3Element(history.state, selected.element.id, { block } as Partial<ReportDesignerV3Element>);
    commit(next, { coalesce: true });
  }

  function duplicateSelection() {
    if (!editingEnabled) return;
    const next = duplicateSelectedV3Elements(history.state);
    if (next === history.state) {
      setCapacityNotice(getV3ElementCapacityIssue(history.state, undefined, Math.max(1, history.state.selectedIds.length)) ?? "当前选择无法复制。");
      return;
    }
    setCapacityNotice(null);
    commit(next);
  }

  function alignSelection(alignment: Parameters<typeof alignSelectedV3Elements>[1]) {
    if (!editingEnabled || history.state.selectedIds.length < 2) return;
    commit(alignSelectedV3Elements(history.state, alignment));
  }

  function distributeSelection(direction: Parameters<typeof distributeSelectedV3Elements>[1]) {
    if (!editingEnabled || history.state.selectedIds.length < 3) return;
    commit(distributeSelectedV3Elements(history.state, direction));
  }

  function handleCommitTransform(baseState: ReportDesignerV3DocumentState, transform: ReportDesignerV3Transform, deltaX: number, deltaY: number) {
    if (!editingEnabled) return;
    const next = transform.kind === "move"
      ? moveSelectedV3Elements(baseState, deltaX, deltaY)
      : resizeV3Element(baseState, transform.elementId, transform.direction, deltaX, deltaY);
    history.commitFrom(baseState, next);
  }

  function handleCancelTransform(baseState: ReportDesignerV3DocumentState) {
    if (!editingEnabled) return;
    history.preview(baseState);
  }

  // Keyboard listeners are installed once per workspace.  Refs keep the
  // handler on the hot path stable while still reading the latest history and
  // migration-confirmation state on every key press.
  historyRef.current = history;
  editingEnabledRef.current = editingEnabled;
  commitRef.current = commit;
  clearSelectionRef.current = clearSelection;
  duplicateSelectionRef.current = duplicateSelection;

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (isEditableTarget(event.target)) return;
      const currentHistory = historyRef.current;
      const canEdit = editingEnabledRef.current;
      const modifier = event.ctrlKey || event.metaKey;
      if (event.key === "Escape") {
        clearSelectionRef.current();
        return;
      }
      if ((event.key === "Delete" || event.key === "Backspace") && canEdit && currentHistory.state.selectedIds.length > 0) {
        event.preventDefault();
        commitRef.current(deleteSelectedV3Elements(currentHistory.state));
        return;
      }
      if (modifier && event.key.toLowerCase() === "z") {
        if (!canEdit) return;
        event.preventDefault();
        event.shiftKey ? currentHistory.redo() : currentHistory.undo();
        return;
      }
      if (modifier && event.key.toLowerCase() === "y") {
        if (!canEdit) return;
        event.preventDefault();
        currentHistory.redo();
        return;
      }
      if (modifier && event.key.toLowerCase() === "d" && canEdit && currentHistory.state.selectedIds.length > 0) {
        event.preventDefault();
        duplicateSelectionRef.current();
        return;
      }
      if (canEdit && currentHistory.state.selectedIds.length > 0 && ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(event.key)) {
        event.preventDefault();
        const step = event.shiftKey ? 500 : 100;
        const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
        const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
        commitRef.current(moveSelectedV3Elements(currentHistory.state, dx, dy, false));
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  return (
    <section className="report-designer-v3-workspace" aria-label="报表模板 V3 自由画布设计器">
      <header className="report-designer-v3-header">
        <div>
          <span className="report-designer-v3-eyebrow">V3 自由画布</span>
          <h2>{displayName || "报表模板"}</h2>
          <p>A4 固定页面 · {history.state.schema.page.orientation === "Landscape" ? "横版 297 × 210 mm" : "竖版 210 × 297 mm"} · 坐标精度 0.01 mm</p>
        </div>
        <div className="report-designer-v3-header-actions">
           <button className="command-button secondary" type="button" onClick={() => { history.reset(parsed.schema); setDraftEnabled(false); setMigrationAccepted(!parsed.migrated); onDesignerDraftContentChange?.(""); }}>
            <RotateCcw size={16} aria-hidden="true" />
            <span>重新载入</span>
          </button>
          <span className="report-designer-v3-element-count">{countElements(history.state.schema)} 个元素</span>
        </div>
      </header>

      {parsed.issues.length > 0 ? (
        <div className={parsed.issues.some((issue) => issue.severity === "error") ? "report-designer-v3-notice error" : "report-designer-v3-notice warning"} role="status">
          <strong>{parsed.issues.some((issue) => issue.severity === "error") ? "模板结构需要处理" : parsed.migrated ? "模板已规范化，请复核" : "模板存在校验提示"}</strong>
          <span>{parsed.issues.slice(0, 3).map((issue) => issue.message).join("；")}</span>
          {parsed.issues.length > 3 ? <small>还有 {parsed.issues.length - 3} 项提示</small> : null}
        </div>
      ) : null}

      {draftEnabled && exportValidation.blocked ? (
        <div className="report-designer-v3-notice error" role="alert">
          <strong>当前草稿不能保存</strong>
          <span>{exportValidation.issues.filter((issue) => issue.severity === "error").slice(0, 3).map((issue) => issue.message).join("；") || "请修正设计结构后再保存。"}</span>
          {exportValidation.issues.filter((issue) => issue.severity === "error").length > 3 ? <small>还有更多阻断问题，请逐项检查右侧属性。</small> : null}
        </div>
      ) : null}

      {legacyMigrationPending && !migrationAccepted ? (
        <div className="report-designer-v3-notice warning" role="status">
          <strong>{migrationNoticeTitle(parsed.sourceVersion, parsed.issues.some((issue) => issue.severity === "error"))}</strong>
          <span>{migrationNoticeDescription(parsed.sourceVersion)}</span>
          <button className="command-button secondary" type="button" onClick={enableDraftEditing}>
            开始 V3 编辑
          </button>
        </div>
      ) : null}

      {capacityNotice ? <div className="report-designer-v3-notice warning" role="status"><strong>已达到设计器限制</strong><span>{capacityNotice}</span></div> : null}

      <fieldset className="report-designer-v3-editing-surface" disabled={!editingEnabled}>
      <div className="report-designer-v3-toolbar" role="toolbar" aria-label="设计器工具栏">
        <div className="report-designer-v3-toolbar-group">
          <ToolbarButton label="文本" icon={<Pilcrow size={15} />} onClick={() => placeElement(createV3TextElement())} />
          <ToolbarButton label="字段" icon={<Plus size={15} />} onClick={() => fieldGroups[0]?.fields[0] && insertField(fieldGroups[0].fields[0])} disabled={fieldGroups.length === 0} />
          <ToolbarButton label="矩形" icon={<span className="report-designer-v3-tool-glyph">□</span>} onClick={() => placeElement(createV3RectangleElement())} />
          <ToolbarButton label="线" icon={<span className="report-designer-v3-tool-glyph">╱</span>} onClick={() => placeElement(createV3LineElement())} />
          {reportType === "ExportDocument" ? <ToolbarButton label="图片" icon={<ImageIcon size={15} />} onClick={() => placeElement(createV3ImageElement())} /> : null}
        </div>
        <div className="report-designer-v3-toolbar-group">
          <ToolbarButton label="行" icon={<span>行</span>} onClick={() => insertFlow(createRowBlock(reportType))} />
          <ToolbarButton label="票据格" icon={<Grid2X2 size={15} />} onClick={() => insertFlow(createGridBlock(reportType))} />
          <ToolbarButton label="条件" icon={<ListFilter size={15} />} onClick={() => insertFlow(createConditionalBlock(reportType))} />
          {reportType === "ExportDocument" ? <ToolbarButton label="明细表" icon={<Table2 size={15} />} onClick={() => insertFlow(createDetailTableBlock())} /> : null}
          <ToolbarButton label="分页" icon={<FilePlus2 size={15} />} onClick={() => insertFlow(createPageBreakBlock())} />
        </div>
        <div className="report-designer-v3-toolbar-group report-designer-v3-toolbar-group-end">
          <ToolbarButton label="撤销" icon={<Undo2 size={15} />} onClick={history.undo} disabled={!editingEnabled || !history.canUndo} />
          <ToolbarButton label="重做" icon={<Redo2 size={15} />} onClick={history.redo} disabled={!editingEnabled || !history.canRedo} />
           <ToolbarButton label="复制" icon={<Copy size={15} />} onClick={duplicateSelection} disabled={history.state.selectedIds.length === 0 || !editingEnabled} />
           <ToolbarButton label="删除" icon={<Trash2 size={15} />} onClick={() => commit(deleteSelectedV3Elements(history.state))} disabled={history.state.selectedIds.length === 0 || !editingEnabled} danger />
          <div className="report-designer-v3-arrangement-group" role="group" aria-label="对齐与分布">
            <ToolbarButton label="左对齐" icon={<AlignHorizontalJustifyStart size={15} />} onClick={() => alignSelection("left")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="水平居中" icon={<AlignHorizontalJustifyCenter size={15} />} onClick={() => alignSelection("center-horizontal")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="右对齐" icon={<AlignHorizontalJustifyEnd size={15} />} onClick={() => alignSelection("right")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="顶端对齐" icon={<AlignVerticalJustifyStart size={15} />} onClick={() => alignSelection("top")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="垂直居中" icon={<AlignVerticalJustifyCenter size={15} />} onClick={() => alignSelection("center-vertical")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="底端对齐" icon={<AlignVerticalJustifyEnd size={15} />} onClick={() => alignSelection("bottom")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
            <ToolbarButton label="水平分布" icon={<ArrowLeftRight size={15} />} onClick={() => distributeSelection("horizontal")} disabled={history.state.selectedIds.length < 3 || !editingEnabled} />
            <ToolbarButton label="垂直分布" icon={<ArrowUpDown size={15} />} onClick={() => distributeSelection("vertical")} disabled={history.state.selectedIds.length < 3 || !editingEnabled} />
          </div>
          <ToolbarButton label="缩小" icon={<ZoomOut size={15} />} onClick={() => setZoom((value) => Math.max(0.45, Number((value - 0.05).toFixed(2))))} />
          <span className="report-designer-v3-zoom-readout">{Math.round(zoom * 100)}%</span>
          <ToolbarButton label="放大" icon={<ZoomIn size={15} />} onClick={() => setZoom((value) => Math.min(1.5, Number((value + 0.05).toFixed(2))))} />
        </div>
      </div>
      </fieldset>

      <fieldset className="report-designer-v3-editing-surface" disabled={!editingEnabled}>
      <div className="report-designer-v3-layout">
        <aside className="report-designer-v3-sidebar">
          <div className="report-designer-v3-sidebar-tabs" role="tablist" aria-label="设计器资源面板">
            <TabButton active={sidebarTab === "components"} label="组件" onClick={() => setSidebarTab("components")} />
            <TabButton active={sidebarTab === "fields"} label="字段" onClick={() => setSidebarTab("fields")} />
            <TabButton active={sidebarTab === "layers"} label="图层" onClick={() => setSidebarTab("layers")} />
          </div>
          {sidebarTab === "components" ? <ComponentPalette reportType={reportType} onText={() => placeElement(createV3TextElement())} onRectangle={() => placeElement(createV3RectangleElement())} onLine={() => placeElement(createV3LineElement())} onImage={reportType === "ExportDocument" ? () => placeElement(createV3ImageElement()) : undefined} onRow={() => insertFlow(createRowBlock(reportType))} onGrid={() => insertFlow(createGridBlock(reportType))} onConditional={() => insertFlow(createConditionalBlock(reportType))} onDetailTable={reportType === "ExportDocument" ? () => insertFlow(createDetailTableBlock()) : undefined} onPageBreak={() => insertFlow(createPageBreakBlock())} /> : null}
          {sidebarTab === "fields" ? (
            <FieldPanel query={fieldQuery} groups={visibleFieldGroups} onQueryChange={setFieldQuery} onInsert={insertField} />
          ) : null}
          {sidebarTab === "layers" ? <LayerPanel state={history.state} onSelect={(id) => history.select(history.state.selectedIds, id)} onCommit={commit} /> : null}
        </aside>

        <main className="report-designer-v3-canvas-column">
          <div className="report-designer-v3-canvas-meta">
            <span>页面：A4 {history.state.schema.page.orientation === "Landscape" ? "横版" : "竖版"}</span>
            <span>{pageSize.widthMm} × {pageSize.heightMm} mm</span>
            <label><input type="checkbox" checked={history.state.schema.grid.enabled} onChange={(event) => commit(updateV3Grid(history.state, { enabled: event.target.checked }))} /> 网格</label>
            <label><input type="checkbox" checked={history.state.schema.grid.snap} onChange={(event) => commit(updateV3Grid(history.state, { snap: event.target.checked }))} /> 吸附</label>
          </div>
           <ReportDesignerV3Canvas
             state={history.state}
             zoom={zoom}
             disabled={!editingEnabled}
            onSelect={selectElement}
             onCommitTransform={handleCommitTransform}
             onCancelTransform={handleCancelTransform}
             onClearSelection={clearSelection}
          />
        </main>

        <aside className="report-designer-v3-inspector">
          {selected ? (
            <ElementInspector
              located={selected}
              fieldGroups={fieldGroups}
              onPatch={patchSelected}
              onPatchStyle={patchSelectedStyle}
              onCommit={commit}
              state={history.state}
              onFlowCommit={patchSelectedFlow}
              onZIndex={(direction) => commit(setV3ElementZIndex(history.state, selected.element.id, direction))}
            />
          ) : (
            <PageInspector state={history.state} onCommit={commit} />
          )}
        </aside>
      </div>
      </fieldset>
    </section>
  );
}

function ToolbarButton({ label, icon, onClick, disabled, danger }: { label: string; icon: React.ReactNode; onClick: () => void; disabled?: boolean; danger?: boolean }) {
  return <button className={`report-designer-v3-tool-button${danger ? " is-danger" : ""}`} type="button" title={label} aria-label={label} onClick={onClick} disabled={disabled}>{icon}<span>{label}</span></button>;
}

function TabButton({ active, label, onClick }: { active: boolean; label: string; onClick: () => void }) {
  return <button className={active ? "is-active" : ""} type="button" role="tab" aria-selected={active} onClick={onClick}>{label}</button>;
}

function ComponentPalette({ reportType, onText, onRectangle, onLine, onImage, onRow, onGrid, onConditional, onDetailTable, onPageBreak }: { reportType: ReportDesignerReportType; onText: () => void; onRectangle: () => void; onLine: () => void; onImage?: () => void; onRow: () => void; onGrid: () => void; onConditional: () => void; onDetailTable?: () => void; onPageBreak: () => void }) {
  return <div className="report-designer-v3-panel-content">
    <PaletteSection title="基础">
      <PaletteAction label="文本" onClick={onText} icon={<Pilcrow size={15} />} />
      <PaletteAction label="矩形" onClick={onRectangle} icon={<span>□</span>} />
      <PaletteAction label="线条" onClick={onLine} icon={<span>╱</span>} />
      {onImage ? <PaletteAction label="图片/印章" onClick={onImage} icon={<ImageIcon size={15} />} /> : null}
    </PaletteSection>
    <PaletteSection title="业务组件">
      <PaletteAction label="多列行" onClick={onRow} icon={<span>行</span>} />
      <PaletteAction label="票据格" onClick={onGrid} icon={<Grid2X2 size={15} />} />
      <PaletteAction label="条件块" onClick={onConditional} icon={<ListFilter size={15} />} />
      {onDetailTable ? <PaletteAction label="明细表" onClick={onDetailTable} icon={<Table2 size={15} />} /> : null}
    </PaletteSection>
    <PaletteSection title="打印">
      <PaletteAction label="分页符" onClick={onPageBreak} icon={<FilePlus2 size={15} />} />
    </PaletteSection>
    <div className="report-designer-v3-help">先选中元素，再在右侧输入精确坐标。页面始终是 A4，横竖版切换会自动限制元素在页面内。</div>
    <div className="report-designer-v3-report-type">当前数据域：{reportType === "PaymentVoucher" ? "付款/报销" : "出口单据"}</div>
  </div>;
}

function PaletteSection({ title, children }: { title: string; children: React.ReactNode }) {
  return <section className="report-designer-v3-palette-section"><h3>{title}</h3><div className="report-designer-v3-palette-grid">{children}</div></section>;
}

function PaletteAction({ label, icon, onClick }: { label: string; icon: React.ReactNode; onClick: () => void }) {
  return <button className="report-designer-v3-palette-action" type="button" onClick={onClick}>{icon}<span>{label}</span></button>;
}

function FieldPanel({ query, groups, onQueryChange, onInsert }: { query: string; groups: ReportDesignerFieldGroup[]; onQueryChange: (value: string) => void; onInsert: (field: { label: string; value: string }) => void }) {
  return <div className="report-designer-v3-panel-content">
    <label className="report-designer-v3-field-search"><span>搜索字段</span><input value={query} placeholder="发票号、客户、金额..." onChange={(event) => onQueryChange(event.target.value)} /></label>
    {groups.length === 0 ? <p className="report-designer-v3-muted">暂无可用字段</p> : groups.map((group) => <details key={group.category} open={Boolean(query.trim()) || groups.length <= 4}><summary>{group.category}<small>{group.fields.length}</small></summary><div className="report-designer-v3-field-list">{group.fields.map((field) => <button type="button" key={field.value} title={field.value} onClick={() => onInsert(field)}><span>{field.label}</span><small>{field.value}</small></button>)}</div></details>)}
  </div>;
}

function LayerPanel({ state, onSelect, onCommit }: { state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void }) {
  return <div className="report-designer-v3-panel-content report-designer-v3-layer-list">
    <div className="report-designer-v3-panel-caption"><Layers3 size={15} aria-hidden="true" /><span>图层与元素</span></div>
    {state.schema.layers.map((layer) => <LayerRow key={layer.id} layer={layer} state={state} onSelect={onSelect} onCommit={onCommit} />)}
  </div>;
}

function LayerRow({ layer, state, onSelect, onCommit }: { layer: ReportDesignerV3Layer; state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void }) {
  return <section className={`report-designer-v3-layer-row${state.activeLayerId === layer.id ? " is-active" : ""}`}>
    <div className="report-designer-v3-layer-heading">
      <button className="report-designer-v3-layer-name" type="button" onClick={() => onSelect(layer.id)}><span>{layer.name}</span><small>{layer.elements.length}</small></button>
      <button className="report-designer-v3-icon-button" type="button" title={layer.visible ? "隐藏图层" : "显示图层"} aria-label={layer.visible ? "隐藏图层" : "显示图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { visible: !layer.visible }))}>{layer.visible ? <Eye size={15} /> : <EyeOff size={15} />}</button>
      <button className="report-designer-v3-icon-button" type="button" title={layer.locked ? "解锁图层" : "锁定图层"} aria-label={layer.locked ? "解锁图层" : "锁定图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { locked: !layer.locked }))}>{layer.locked ? <Lock size={15} /> : <Unlock size={15} />}</button>
    </div>
    <LayerPrintControls layer={layer} state={state} onCommit={onCommit} />
    {layer.elements.length > 0 ? <div className="report-designer-v3-layer-elements">{[...layer.elements].sort((a, b) => b.zIndex - a.zIndex).map((element) => <button className={state.selectedIds.includes(element.id) ? "is-selected" : ""} type="button" key={element.id} onClick={() => onCommit({ ...state, selectedIds: [element.id], activeLayerId: layer.id })}><span>{reportDesignerV3ElementText(element) || element.type}</span><small>{element.locked ? "锁定" : `${hundredthMmToMm(element.xHundredthMm).toFixed(1)}, ${hundredthMmToMm(element.yHundredthMm).toFixed(1)}`}</small></button>)}</div> : <p className="report-designer-v3-muted">空图层</p>}
  </section>;
}

function LayerPrintControls({ layer, state, onCommit }: { layer: ReportDesignerV3Layer; state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void }) {
  const print = layer.print;
  function patchPrint(update: Partial<typeof print>) {
    onCommit(updateV3Layer(state, layer.id, { print: { ...print, ...update } }));
  }
  return <details className="report-designer-v3-layer-print"><summary>打印行为</summary><div className="report-designer-v3-layer-print-fields">
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={print.repeatOnEveryPage} disabled={layer.role === "Body"} onChange={(event) => patchPrint({ repeatOnEveryPage: event.target.checked })} /><span>每页重复{layer.role === "Body" ? "（主体不支持）" : ""}</span></label>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={print.keepTogether} onChange={(event) => patchPrint({ keepTogether: event.target.checked })} /><span>保持图层完整</span></label>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={print.pinToPageBottom} disabled={layer.role !== "Footer"} onChange={(event) => patchPrint({ pinToPageBottom: event.target.checked })} /><span>页脚贴底{layer.role !== "Footer" ? "（仅页脚）" : ""}</span></label>
    <NumberField label="最小高度 (mm)" value={hundredthMmToMm(print.minHeightHundredthMm)} min={0} max={260} onCommit={(value) => patchPrint({ minHeightHundredthMm: Math.round(value * 100) })} />
  </div></details>;
}

function PageInspector({ state, onCommit }: { state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void }) {
  const page = state.schema.page;
  return <div className="report-designer-v3-inspector-content">
    <InspectorTitle title="页面设置" subtitle="固定 A4 画布" />
    <div className="report-designer-v3-orientation-control"><span>方向</span><div><button className={page.orientation === "Portrait" ? "is-active" : ""} type="button" onClick={() => onCommit(updateV3Page(state, { orientation: "Portrait" }))}>竖版</button><button className={page.orientation === "Landscape" ? "is-active" : ""} type="button" onClick={() => onCommit(updateV3Page(state, { orientation: "Landscape" }))}>横版</button></div></div>
    <div className="report-designer-v3-page-size-readout"><strong>A4</strong><span>{page.orientation === "Landscape" ? "297 × 210 mm" : "210 × 297 mm"}</span></div>
    <div className="report-designer-v3-inspector-grid">
      <NumberField label="上边距" value={hundredthMmToMm(page.marginTopHundredthMm)} onCommit={(value) => onCommit(updateV3Page(state, { marginTopHundredthMm: Math.round(value * 100) }))} />
      <NumberField label="右边距" value={hundredthMmToMm(page.marginRightHundredthMm)} onCommit={(value) => onCommit(updateV3Page(state, { marginRightHundredthMm: Math.round(value * 100) }))} />
      <NumberField label="下边距" value={hundredthMmToMm(page.marginBottomHundredthMm)} onCommit={(value) => onCommit(updateV3Page(state, { marginBottomHundredthMm: Math.round(value * 100) }))} />
      <NumberField label="左边距" value={hundredthMmToMm(page.marginLeftHundredthMm)} onCommit={(value) => onCommit(updateV3Page(state, { marginLeftHundredthMm: Math.round(value * 100) }))} />
      <NumberField label="网格间距" value={hundredthMmToMm(state.schema.grid.sizeHundredthMm)} min={1} max={50} onCommit={(value) => onCommit(updateV3Grid(state, { sizeHundredthMm: Math.max(100, Math.round(value * 100)) }))} />
    </div>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={state.schema.grid.enabled} onChange={(event) => onCommit(updateV3Grid(state, { enabled: event.target.checked }))} /><span>显示网格</span></label>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={state.schema.grid.snap} onChange={(event) => onCommit(updateV3Grid(state, { snap: event.target.checked }))} /><span>拖动时吸附网格</span></label>
    <div className="report-designer-v3-inspector-tip">页眉、主体、页脚和覆盖层分别位于独立图层；锁定后仍可预览和输出，但不会被误移动。</div>
  </div>;
}

function ElementInspector({ located, fieldGroups, state, onPatch, onPatchStyle, onCommit, onFlowCommit, onZIndex }: { located: NonNullable<ReturnType<typeof findV3Element>>; fieldGroups: ReportDesignerFieldGroup[]; state: ReportDesignerV3DocumentState; onPatch: (update: Partial<ReportDesignerV3Element>) => void; onPatchStyle: (update: Partial<ReportDesignerV3Element["style"]>) => void; onCommit: (next: ReportDesignerV3DocumentState) => void; onFlowCommit: (block: Extract<ReportBlock, { type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak" }>) => void; onZIndex: (direction: "front" | "back" | "forward" | "backward") => void }) {
  const { element, layer } = located;
  const editable = !element.locked && !layer.locked;
  const controlledImageFields = getControlledReportImageFieldPaths(state.schema.reportType);
  const currentImageField = element.type === "Image" && isControlledReportImageFieldPath(element.fieldPath)
    ? element.fieldPath
    : "";
  return <div className="report-designer-v3-inspector-content">
    <InspectorTitle title="元素属性" subtitle={reportDesignerV3ElementText(element) || element.type} />
    <div className="report-designer-v3-element-type-badge">{element.type}{element.type === "Flow" ? ` · ${element.flowKind}` : ""}</div>
    <div className="report-designer-v3-inspector-grid">
      <NumberField label="X (mm)" value={hundredthMmToMm(element.xHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ xHundredthMm: Math.round(value * 100) })} />
      <NumberField label="Y (mm)" value={hundredthMmToMm(element.yHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ yHundredthMm: Math.round(value * 100) })} />
      <NumberField label="宽 (mm)" value={hundredthMmToMm(element.widthHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ widthHundredthMm: Math.round(value * 100) })} />
      <NumberField label="高 (mm)" value={hundredthMmToMm(element.heightHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ heightHundredthMm: Math.round(value * 100) })} />
      <NumberField label="旋转角度 (°)" value={element.rotationDeg} min={-360} max={360} disabled={!editable} onCommit={(value) => onPatch({ rotationDeg: Math.round(value * 100) / 100 })} />
    </div>
    {element.type === "Text" ? <label className="report-designer-v3-wide-field"><span>文本</span><TextCommitField value={element.text} multiline disabled={!editable} onCommit={(value) => onPatch({ text: value })} /></label> : null}
    {element.type === "Field" ? <><label><span>字段</span><select value={element.fieldPath} disabled={!editable} onChange={(event) => onPatch({ fieldPath: event.target.value })}>{flattenFields(fieldGroups).map((field) => <option key={field.value} value={field.value}>{field.label} · {field.value}</option>)}</select></label><label><span>占位文本</span><TextCommitField value={element.fallbackText ?? ""} disabled={!editable} onCommit={(value) => onPatch({ fallbackText: value || undefined })} /></label></> : null}
    {element.type === "Image" ? <><label><span>来源</span><select value={element.sourceKind} disabled={!editable} onChange={(event) => {
      const sourceKind = event.target.value === "Resource" ? "Resource" : "Field";
      onPatch(sourceKind === "Field"
        ? { sourceKind, fieldPath: currentImageField || controlledImageFields[0] }
        : { sourceKind, fieldPath: undefined });
    }}><option value="Field">字段图片</option><option value="Resource">受控资源</option></select></label>{element.sourceKind === "Field" ? <label><span>图片字段</span><select value={currentImageField} disabled={!editable || controlledImageFields.length === 0} onChange={(event) => onPatch({ fieldPath: event.target.value || undefined })}><option value="">请选择受控图片字段</option>{controlledImageFields.map((fieldPath) => <option key={fieldPath} value={fieldPath}>{fieldPath}</option>)}</select>{controlledImageFields.length === 0 ? <small className="report-designer-v3-muted">当前报表类型没有可绑定的受控图片字段。</small> : null}</label> : <label><span>资源 ID</span><TextCommitField value={element.resourceId ?? ""} disabled={!editable} placeholder="上传资源后填写" onCommit={(value) => onPatch({ resourceId: value || undefined })} /></label>}</> : null}
    {element.type === "Line" ? <label><span>方向</span><select value={element.direction} disabled={!editable} onChange={(event) => onPatch({ direction: event.target.value === "Vertical" ? "Vertical" : "Horizontal" })}><option value="Horizontal">水平</option><option value="Vertical">垂直</option></select></label> : null}
    {element.type === "Flow" ? <>
      <div className="report-designer-v3-inspector-tip">结构化业务组件仍使用统一校验和打印渲染规则；你可以直接编辑列、条件、明细和分页设置。</div>
      <ReportDesignerV3FlowProperties block={element.block} fieldGroups={fieldGroups} onCommit={onFlowCommit} />
    </> : null}
    <div className="report-designer-v3-style-editor">
      <strong>样式</strong>
      <div className="report-designer-v3-inspector-grid"><NumberField label="字号 pt" value={element.style.fontSizePt ?? 10} min={6} max={96} disabled={!editable} onCommit={(value) => onPatchStyle({ fontSizePt: value })} /><label><span>对齐</span><select disabled={!editable} value={element.style.align ?? "Left"} onChange={(event) => onPatchStyle({ align: event.target.value as "Left" | "Center" | "Right" })}><option value="Left">左</option><option value="Center">中</option><option value="Right">右</option></select></label></div>
      <label className="report-designer-v3-check-row"><input type="checkbox" checked={element.style.bold === true} disabled={!editable} onChange={(event) => onPatchStyle({ bold: event.target.checked })} /><span>粗体</span></label>
      <label><span>文字颜色</span><TextCommitField value={element.style.color ?? "#1f2933"} disabled={!editable} onCommit={(value) => onPatchStyle({ color: value })} /></label>
      <label><span>背景颜色</span><TextCommitField value={element.style.backgroundColor ?? ""} disabled={!editable} placeholder="留空表示透明" onCommit={(value) => onPatchStyle({ backgroundColor: value || undefined })} /></label>
    </div>
    <div className="report-designer-v3-element-actions"><button type="button" onClick={() => onZIndex("back")} disabled={!editable}><ArrowDown size={15} />置底</button><button type="button" onClick={() => onZIndex("backward")} disabled={!editable}><ArrowDown size={15} />后移</button><button type="button" onClick={() => onZIndex("forward")} disabled={!editable}><ArrowUp size={15} />前移</button><button type="button" onClick={() => onZIndex("front")} disabled={!editable}><ArrowUp size={15} />置顶</button></div>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={element.visible} onChange={(event) => onCommit(updateV3Element(state, element.id, { visible: event.target.checked }))} /><span>在画布中显示</span></label>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={element.outputEnabled} onChange={(event) => onCommit(updateV3Element(state, element.id, { outputEnabled: event.target.checked }))} /><span>参与打印输出</span></label>
    <label className="report-designer-v3-check-row"><input type="checkbox" checked={element.locked} disabled={layer.locked} onChange={(event) => onCommit(updateV3Element(state, element.id, { locked: event.target.checked }))} /><span>锁定元素</span></label>
    {layer.locked ? <div className="report-designer-v3-lock-note"><Lock size={14} />图层已锁定，请先在图层面板解锁。</div> : null}
  </div>;
}

function InspectorTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return <div className="report-designer-v3-inspector-title"><strong>{title}</strong><span>{subtitle}</span></div>;
}

function NumberField({ label, value, onCommit, min = 0, max = 1000, disabled = false }: { label: string; value: number; onCommit: (value: number) => void; min?: number; max?: number; disabled?: boolean }) {
  const [draft, setDraft] = useState(formatNumber(value));
  useEffect(() => setDraft(formatNumber(value)), [value]);
  function commit() {
    const parsed = Number(draft);
    if (!Number.isFinite(parsed)) {
      setDraft(formatNumber(value));
      return;
    }
    const next = Math.min(max, Math.max(min, parsed));
    setDraft(formatNumber(next));
    if (next !== value) onCommit(next);
  }
  return <label><span>{label}</span><input type="number" inputMode="decimal" step="0.01" min={min} max={max} disabled={disabled} value={draft} onChange={(event) => setDraft(event.target.value)} onBlur={commit} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); event.currentTarget.blur(); } }} /></label>;
}

function TextCommitField({
  value,
  onCommit,
  disabled = false,
  multiline = false,
  placeholder,
}: {
  value: string;
  onCommit: (value: string) => void;
  disabled?: boolean;
  multiline?: boolean;
  placeholder?: string;
}) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);

  function commit() {
    if (draft !== value) onCommit(draft);
  }

  const commonProps = {
    disabled,
    placeholder,
    value: draft,
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft(event.target.value),
    onBlur: commit,
    onKeyDown: (event: React.KeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      if (event.key === "Escape") {
        event.preventDefault();
        setDraft(value);
        event.currentTarget.blur();
      } else if (event.key === "Enter" && (!multiline || event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        event.currentTarget.blur();
      }
    },
  };

  return multiline
    ? <textarea {...commonProps} rows={3} />
    : <input {...commonProps} type="text" />;
}
