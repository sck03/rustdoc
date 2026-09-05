import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  AlignHorizontalJustifyCenter, AlignHorizontalJustifyEnd, AlignHorizontalJustifyStart,
  AlignVerticalJustifyCenter, AlignVerticalJustifyEnd, AlignVerticalJustifyStart,
  ArrowDown, ArrowUp, ArrowLeftRight, ArrowUpDown,
  Braces, ClipboardPaste, Columns3, Copy, FilePlus2, Files, Grid2X2, Hash,
  Image as ImageIcon, ListFilter, Maximize2, Pilcrow, Redo2, RotateCcw,
  Table2, Trash2, Undo2, ZoomIn, ZoomOut,
} from "lucide-react";
import type {
  ApiReportTemplateFieldCatalogResponse,
  ApiReportTemplateImageResourceResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { buildReportDesignerFieldGroups } from "./reportDesignerFields.ts";
import { useReportDesignerV3History } from "./reportDesignerV3History.ts";
import {
  exportReportDesignerV3SchemaToHtml,
  validateReportDesignerV3Export,
} from "./reportDesignerV3HtmlExporter.ts";
import {
  createV3FieldElement, createV3FlowElement, createV3ImageElement, createV3LineElement,
  createV3PageNumberElement, createV3RectangleElement, createV3TextElement,
  alignSelectedV3Elements, deleteSelectedV3Elements, duplicateSelectedV3Elements,
  distributeSelectedV3Elements, getV3ElementCapacityIssue, findV3Element, insertV3Element,
  moveSelectedV3Elements, pasteV3Elements, resizeV3Element, selectAllV3Elements, setV3ElementZIndex,
  toggleV3Selection, updateV3Element, updateV3Grid, type ReportDesignerV3DocumentState,
} from "./reportDesignerV3Mutations.ts";
import { parseReportDesignerV3FromHtml } from "./reportDesignerV3TemplateParser.ts";
import {
  reportDesignerV3PageSize,
  type ReportDesignerV3Element,
} from "./reportDesignerV3Schema.ts";
import { ReportDesignerV3Canvas, type ReportDesignerV3Transform } from "./ReportDesignerV3Canvas.tsx";
import { setReportDesignerLayerRoleHeight } from "./reportDesignerLayerBands.ts";
import type { ReportBlock, ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { createConditionalBlock, createDetailTableBlock, createGridBlock, createPageBreakBlock, createRowBlock } from "./reportDesignerBlockFactories.ts";
import {
  countElements,
  clampReportDesignerV3Zoom,
  filterFieldGroups,
  fitReportDesignerV3Zoom,
  isEditableTarget,
  migrationNoticeDescription,
  migrationNoticeTitle,
  REPORT_DESIGNER_V3_ZOOM_PRESETS,
} from "./reportDesignerV3WorkspaceHelpers.tsx";
import {
  ComponentPalette,
  ElementInspector,
  FieldPanel,
  focusDesignerNode,
  LayerPanel,
  MultiElementInspector,
  type PaletteActions,
  PageInspector,
} from "./ReportDesignerV3Panels.tsx";

type V3SidebarTab = "components" | "fields" | "layers";

export function ReportDesignerV3Workspace({
  reportType,
  displayName,
  content,
  fieldCatalog,
  client,
  editable,
  onDesignerDraftContentChange,
}: {
  reportType: ReportDesignerReportType;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null;
  client?: ExportDocManagerApiClient;
  editable: boolean;
  onDesignerDraftContentChange?: (nextContent: string) => void;
}) {
  const parsed = useMemo(() => parseReportDesignerV3FromHtml(content, reportType), [content, reportType]);
  const history = useReportDesignerV3History(parsed.schema);
  const fieldGroups = useMemo(() => buildReportDesignerFieldGroups(fieldCatalog, reportType), [fieldCatalog, reportType]);
  const [sidebarTab, setSidebarTab] = useState<V3SidebarTab>("components");
  const [zoom, setZoom] = useState(0.72);
  const [fitRequest, setFitRequest] = useState(0);
  const [showGuides, setShowGuides] = useState(true);
  const [fieldQuery, setFieldQuery] = useState("");
  const [fieldFocusRequest, setFieldFocusRequest] = useState(0);
  const [capacityNotice, setCapacityNotice] = useState<string | null>(null);
  const [hasClipboard, setHasClipboard] = useState(false);
  const [gridCellSelection, setGridCellSelection] = useState<{ elementId: string; cellId: string } | null>(null);
  const clipboardRef = useRef<ReportDesignerV3Element[]>([]);
  function copySelection() {
    const items = history.state.selectedIds
      .map((id) => findV3Element(history.state.schema, id)?.element)
      .filter((el): el is ReportDesignerV3Element => Boolean(el));
    if (items.length > 0) { clipboardRef.current = items; setHasClipboard(true); }
  }
  function pasteClipboard() {
    if (!editingEnabled) return;
    if (clipboardRef.current.length > 0) {
      const next = pasteV3Elements(history.state, clipboardRef.current, activeLayerId() ?? undefined);
      if (next === history.state) {
        setCapacityNotice(getV3ElementCapacityIssue(history.state, undefined, Math.max(1, clipboardRef.current.length)) ?? "当前图层无法容纳更多元素。");
        return;
      }
      setCapacityNotice(null);
      commit(next);
      return;
    }
    duplicateSelection();
  }
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
  const activeGridCellSelection = selected?.element.type === "Flow" && selected.element.flowKind === "Grid" && gridCellSelection?.elementId === selected.element.id
    ? gridCellSelection
    : null;
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
    setGridCellSelection(null);
    emittedContent.current = "";
    onDesignerDraftContentChange?.("");
  }, [legacyMigrationPending, content, reportType, onDesignerDraftContentChange]);
  useEffect(() => {
    if (!editable || !migrationAccepted || !draftEnabled) return;
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
  }, [draftEnabled, editable, exportValidation.blocked, exportedHtml, migrationAccepted, onDesignerDraftContentChange]);
  const editingEnabled = editable && (!legacyMigrationPending || migrationAccepted);
  function enableDraftEditing() {
    if (!editable) return;
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
  function selectLayer(layerId: string) {
    history.select([], layerId);
    focusDesignerNode(`[data-v3-layer-id="${CSS.escape(layerId)}"]`);
  }
  function clearSelection() {
    history.select([], history.state.activeLayerId);
  }
  function selectGridCell(elementId: string, cellId: string) {
    setGridCellSelection({ elementId, cellId });
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
  function openFieldPanel() {
    setSidebarTab("fields");
    setFieldFocusRequest((value) => value + 1);
  }
  const handleFitZoom = useCallback((value: number) => setZoom(clampReportDesignerV3Zoom(value)), []);
  const insertionActions: PaletteActions = {
    text: () => placeElement(createV3TextElement()),
    rectangle: () => placeElement(createV3RectangleElement()),
    line: () => placeElement(createV3LineElement()),
    pageNumber: () => placeElement(createV3PageNumberElement()),
    image: reportType === "ExportDocument" ? () => placeElement(createV3ImageElement()) : undefined,
    row: () => insertFlow(createRowBlock(reportType)),
    grid: () => insertFlow(createGridBlock(reportType)),
    conditional: () => insertFlow(createConditionalBlock(reportType)),
    detailTable: reportType === "ExportDocument" ? () => insertFlow(createDetailTableBlock()) : undefined,
    pageBreak: () => insertFlow(createPageBreakBlock()),
  };
  const zoomPercent = Math.round(zoom * 100);
  const zoomOptions = [...new Set([...REPORT_DESIGNER_V3_ZOOM_PRESETS, zoomPercent])].sort((left, right) => left - right);
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
  function bindImageResource(elementId: string, resource: ApiReportTemplateImageResourceResponse) {
    if (!editingEnabled) return;
    const located = findV3Element(history.state.schema, elementId);
    if (!located || located.element.type !== "Image" || located.element.locked || located.layer.locked) return;
    const normalizedResource = {
      id: resource.id,
      mediaType: resource.mediaType as "image/png" | "image/jpeg" | "image/gif" | "image/webp",
      byteLength: resource.byteLength,
      sha256: resource.sha256,
      altText: resource.altText || undefined,
    };
    const stateWithResource = {
      ...history.state,
      schema: {
        ...history.state.schema,
        resources: [
          ...(history.state.schema.resources ?? []).filter((item) => item.id !== normalizedResource.id),
          normalizedResource,
        ],
      },
    };
    commit(updateV3Element(stateWithResource, elementId, {
      sourceKind: "Resource",
      resourceId: normalizedResource.id,
      fieldPath: undefined,
      altText: located.element.altText || normalizedResource.altText,
    }));
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
      if (modifier && event.key.toLowerCase() === "a" && canEdit) {
        event.preventDefault();
        commitRef.current(selectAllV3Elements(currentHistory.state));
        return;
      }
      if (modifier && event.key.toLowerCase() === "c" && canEdit && currentHistory.state.selectedIds.length > 0) {
        event.preventDefault();
        copySelection();
        return;
      }
      if (modifier && event.key.toLowerCase() === "v" && canEdit) {
        event.preventDefault();
        pasteClipboard();
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
          {!editable ? <small>只读预览：当前权限或设备不支持设计操作。</small> : null}
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
          <span>{migrationNoticeDescription(parsed.sourceVersion)} 当前仅提供只读浏览；具备编辑权限时确认后才允许修改。</span>
          {editable ? (
            <button className="command-button secondary" type="button" onClick={enableDraftEditing}>
              开始 V3 编辑
            </button>
          ) : null}
        </div>
      ) : null}
      {capacityNotice ? <div className="report-designer-v3-notice warning" role="status"><strong>已达到设计器限制</strong><span>{capacityNotice}</span></div> : null}
      <div className="report-designer-v3-editing-surface">
      <div className="report-designer-v3-toolbar" role="toolbar" aria-label="设计器工具栏">
        {editingEnabled ? <>
        <div className="report-designer-v3-toolbar-group" role="group" aria-label="插入基础元素">
          <ToolbarButton label="文本" icon={<Pilcrow size={15} />} onClick={insertionActions.text} disabled={!editingEnabled} />
          <ToolbarButton label="选择字段" icon={<Braces size={15} />} onClick={openFieldPanel} />
          {insertionActions.image ? <ToolbarButton label="图片" icon={<ImageIcon size={15} />} onClick={insertionActions.image} disabled={!editingEnabled} /> : null}
          <ToolbarButton label="矩形" icon={<span className="report-designer-v3-tool-glyph">□</span>} onClick={insertionActions.rectangle} disabled={!editingEnabled} />
          <ToolbarButton label="线" icon={<span className="report-designer-v3-tool-glyph">╱</span>} onClick={insertionActions.line} disabled={!editingEnabled} />
          <ToolbarButton label="页码" icon={<Hash size={15} />} onClick={insertionActions.pageNumber} disabled={!editingEnabled} />
        </div>
        <div className="report-designer-v3-toolbar-group" role="group" aria-label="插入结构组件">
          <ToolbarButton label="多列行" icon={<Columns3 size={15} />} onClick={insertionActions.row} disabled={!editingEnabled} />
          <ToolbarButton label="普通表格" icon={<Grid2X2 size={15} />} onClick={insertionActions.grid} disabled={!editingEnabled} />
          <ToolbarButton label="条件块" icon={<ListFilter size={15} />} onClick={insertionActions.conditional} disabled={!editingEnabled} />
          {insertionActions.detailTable ? <ToolbarButton label="明细表" icon={<Table2 size={15} />} onClick={insertionActions.detailTable} disabled={!editingEnabled} /> : null}
          <ToolbarButton label="分页符" icon={<FilePlus2 size={15} />} onClick={insertionActions.pageBreak} disabled={!editingEnabled} />
        </div>
        <div className="report-designer-v3-toolbar-group" role="group" aria-label="编辑">
          <ToolbarButton label="撤销" title="撤销 (Ctrl+Z)" icon={<Undo2 size={15} />} onClick={history.undo} disabled={!editingEnabled || !history.canUndo} />
          <ToolbarButton label="重做" title="重做 (Ctrl+Y)" icon={<Redo2 size={15} />} onClick={history.redo} disabled={!editingEnabled || !history.canRedo} />
          <ToolbarButton label="复制" title="复制所选到剪贴板 (Ctrl+C)" icon={<Copy size={15} />} onClick={copySelection} disabled={history.state.selectedIds.length === 0 || !editingEnabled} />
          <ToolbarButton label="粘贴" title="粘贴剪贴板元素 (Ctrl+V)" icon={<ClipboardPaste size={15} />} onClick={pasteClipboard} disabled={!editingEnabled || (!hasClipboard && history.state.selectedIds.length === 0)} />
          <ToolbarButton label="制作副本" title="原位制作副本 (Ctrl+D)" icon={<Files size={15} />} onClick={duplicateSelection} disabled={history.state.selectedIds.length === 0 || !editingEnabled} />
          <ToolbarButton label="删除" title="删除所选 (Delete)" icon={<Trash2 size={15} />} onClick={() => commit(deleteSelectedV3Elements(history.state))} disabled={history.state.selectedIds.length === 0 || !editingEnabled} danger />
        </div>
        <div className="report-designer-v3-toolbar-group report-designer-v3-arrangement-group" role="group" aria-label="对齐与分布">
          <ToolbarButton label="左对齐" icon={<AlignHorizontalJustifyStart size={15} />} onClick={() => alignSelection("left")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="水平居中" icon={<AlignHorizontalJustifyCenter size={15} />} onClick={() => alignSelection("center-horizontal")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="右对齐" icon={<AlignHorizontalJustifyEnd size={15} />} onClick={() => alignSelection("right")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="顶端对齐" icon={<AlignVerticalJustifyStart size={15} />} onClick={() => alignSelection("top")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="垂直居中" icon={<AlignVerticalJustifyCenter size={15} />} onClick={() => alignSelection("center-vertical")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="底端对齐" icon={<AlignVerticalJustifyEnd size={15} />} onClick={() => alignSelection("bottom")} disabled={history.state.selectedIds.length < 2 || !editingEnabled} />
          <ToolbarButton label="水平分布" icon={<ArrowLeftRight size={15} />} onClick={() => distributeSelection("horizontal")} disabled={history.state.selectedIds.length < 3 || !editingEnabled} />
          <ToolbarButton label="垂直分布" icon={<ArrowUpDown size={15} />} onClick={() => distributeSelection("vertical")} disabled={history.state.selectedIds.length < 3 || !editingEnabled} />
        </div>
        </> : null}
        <div className="report-designer-v3-toolbar-group report-designer-v3-toolbar-group-end" role="group" aria-label="视图缩放">
          <ToolbarButton label="缩小" icon={<ZoomOut size={15} />} onClick={() => setZoom((value) => clampReportDesignerV3Zoom(value - 0.05))} />
          <select className="report-designer-v3-zoom-select" aria-label="选择缩放比例" value={String(zoomPercent)} onChange={(event) => setZoom(clampReportDesignerV3Zoom(Number(event.target.value) / 100))}>{zoomOptions.map((value) => <option key={value} value={value}>{value}%</option>)}</select>
          <span className="report-designer-v3-zoom-readout" aria-live="polite">{zoomPercent}%</span>
          <ToolbarButton label="放大" icon={<ZoomIn size={15} />} onClick={() => setZoom((value) => clampReportDesignerV3Zoom(value + 0.05))} />
          <ToolbarButton label="适合窗口" icon={<Maximize2 size={15} />} onClick={() => setFitRequest((value) => value + 1)} />
        </div>
      </div>
      <div className={`report-designer-v3-layout${editingEnabled ? "" : " is-read-only"}`}>
        {editingEnabled ? <aside className="report-designer-v3-sidebar">
          <div className="report-designer-v3-sidebar-tabs" role="tablist" aria-label="设计器资源面板">
            <TabButton active={sidebarTab === "components"} label="组件" onClick={() => setSidebarTab("components")} />
            <TabButton active={sidebarTab === "fields"} label="字段" onClick={() => setSidebarTab("fields")} />
            <TabButton active={sidebarTab === "layers"} label="图层" onClick={() => setSidebarTab("layers")} />
          </div>
          {sidebarTab === "components" ? <ComponentPalette reportType={reportType} actions={insertionActions} canEdit={editingEnabled} /> : null}
          {sidebarTab === "fields" ? (
            <FieldPanel query={fieldQuery} groups={visibleFieldGroups} focusRequest={fieldFocusRequest} onQueryChange={setFieldQuery} onInsert={insertField} canEdit={editingEnabled} />
          ) : null}
          {sidebarTab === "layers" ? <LayerPanel state={history.state} onSelect={selectLayer} onCommit={commit} canEdit={editingEnabled} /> : null}
        </aside> : null}

        <main className="report-designer-v3-canvas-column">
          <div className="report-designer-v3-canvas-meta">
            <span>页面：A4 {history.state.schema.page.orientation === "Landscape" ? "横版" : "竖版"}</span>
            <span>{pageSize.widthMm} × {pageSize.heightMm} mm</span>
            {editingEnabled ? <>
              <label><input type="checkbox" checked={history.state.schema.grid.enabled} onChange={(event) => commit(updateV3Grid(history.state, { enabled: event.target.checked }))} /> 网格</label>
              <label><input type="checkbox" checked={history.state.schema.grid.snap} onChange={(event) => commit(updateV3Grid(history.state, { snap: event.target.checked }))} /> 吸附</label>
              <label><input type="checkbox" checked={showGuides} onChange={(event) => setShowGuides(event.target.checked)} /> 参考线</label>
            </> : null}
          </div>
          <ReportDesignerV3Canvas
            state={history.state}
            zoom={zoom}
            fitRequest={fitRequest}
            showGuides={editingEnabled && showGuides}
            onFitZoom={handleFitZoom}
            disabled={!editingEnabled}
            onSelect={selectElement}
            selectedGridCell={activeGridCellSelection}
            onSelectGridCell={selectGridCell}
            onCommitTransform={handleCommitTransform}
            onCancelTransform={handleCancelTransform}
            onCommitLayerBand={(role, height) => commit(setReportDesignerLayerRoleHeight(history.state, role, height))}
            onClearSelection={clearSelection}
          />
        </main>

        {editingEnabled ? <aside className="report-designer-v3-inspector">
          {history.state.selectedIds.length > 1 ? (
            <MultiElementInspector
              state={history.state}
              onCommit={commit}
              onAlign={alignSelection}
              onDistribute={distributeSelection}
              onDuplicate={duplicateSelection}
              onDelete={() => commit(deleteSelectedV3Elements(history.state))}
              canEdit={editingEnabled}
            />
          ) : selected ? (
            <ElementInspector
              located={selected}
              fieldGroups={fieldGroups}
              onPatch={patchSelected}
              onPatchStyle={patchSelectedStyle}
              onCommit={commit}
              state={history.state}
              onFlowCommit={patchSelectedFlow}
              selectedGridCellId={activeGridCellSelection?.cellId}
              onSelectGridCell={(cellId) => selectGridCell(selected.element.id, cellId)}
              client={client}
              onImageResourceUploaded={bindImageResource}
              onZIndex={(direction) => commit(setV3ElementZIndex(history.state, selected.element.id, direction))}
              canEdit={editingEnabled}
            />
          ) : (
            <PageInspector state={history.state} onCommit={commit} canEdit={editingEnabled} />
          )}
        </aside> : null}
      </div>
      </div>
    </section>
  );
}

function ToolbarButton({ label, title, icon, onClick, disabled, danger }: { label: string; title?: string; icon: React.ReactNode; onClick: () => void; disabled?: boolean; danger?: boolean }) {
  return <button className={`report-designer-v3-tool-button${danger ? " is-danger" : ""}`} type="button" title={title || label} aria-label={label} onClick={onClick} disabled={disabled}>{icon}<span>{label}</span></button>;
}

function TabButton({ active, label, onClick }: { active: boolean; label: string; onClick: () => void }) {
  return <button className={active ? "is-active" : ""} type="button" role="tab" aria-selected={active} onClick={onClick}>{label}</button>;
}
