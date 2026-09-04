import { useEffect, useRef, useState, type ReactNode } from "react";
import {
  Columns3,
  Eye,
  EyeOff,
  FilePlus2,
  Hash,
  Image as ImageIcon,
  Layers3,
  ListFilter,
  Lock,
  Pilcrow,
  Table2,
  Upload,
  Unlock,
} from "lucide-react";
import type {
  ApiReportTemplateImageResourceResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { CommitTextField, ensureCurrentSelectOption, type SelectOption } from "./ReportDesignerPropertyControls.tsx";
import { ReportDesignerV3ColorField } from "./ReportDesignerV3ColorField.tsx";
import { ReportDesignerV3FlowProperties } from "./ReportDesignerV3FlowProperties.tsx";
import { reportDesignerLayerHeight, resolveReportDesignerLayerBands, setReportDesignerLayerHeight } from "./reportDesignerLayerBands.ts";
import {
  findV3Element,
  updateV3Element,
  updateV3Grid,
  updateV3Layer,
  updateV3Page,
  type ReportDesignerV3DocumentState,
} from "./reportDesignerV3Mutations.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementStyle,
  type ReportDesignerV3ImageResource,
  type ReportDesignerV3Layer,
} from "./reportDesignerV3Schema.ts";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { getControlledReportImageFieldPaths, isControlledReportImageFieldPath } from "./reportDesignerSchemaDomains.ts";
import { flattenFields, formatNumber } from "./reportDesignerV3WorkspaceHelpers.tsx";

type FlowBlock = Extract<ReportBlock, { type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak" }>;
type LocatedElement = NonNullable<ReturnType<typeof findV3Element>>;
export type PaletteActions = {
  text: () => void;
  rectangle: () => void;
  line: () => void;
  pageNumber: () => void;
  row: () => void;
  grid: () => void;
  conditional: () => void;
  pageBreak: () => void;
  image?: () => void;
  detailTable?: () => void;
};

const layerPurposes: Record<ReportDesignerV3Layer["role"], string> = {
  Header: "页眉 · 可调高度/每页重复",
  Body: "主体 · 自动填充剩余页面",
  Footer: "页脚 · 可调高度/贴底",
  Overlay: "覆盖层 · 水印/印章",
};

export function ComponentPalette({ reportType, actions, canEdit = true }: { reportType: ReportDesignerReportType; actions: PaletteActions; canEdit?: boolean }) {
  const base = [
    ["文本", actions.text, <Pilcrow size={15} aria-hidden="true" />],
    ["矩形", actions.rectangle, <span aria-hidden="true">□</span>],
    ["线条", actions.line, <span aria-hidden="true">╱</span>],
    ["页码", actions.pageNumber, <Hash size={15} aria-hidden="true" />],
  ] as const;
  return (
    <div className="report-designer-v3-panel-content">
      <PaletteSection title="基础">
        {base.map(([label, onClick, icon]) => <PaletteAction key={label} label={label} onClick={onClick} icon={icon} disabled={!canEdit} />)}
        {actions.image ? <PaletteAction label="图片/印章" onClick={actions.image} icon={<ImageIcon size={15} aria-hidden="true" />} disabled={!canEdit} /> : null}
      </PaletteSection>
      <PaletteSection title="业务组件">
        <PaletteAction label="多列行" onClick={actions.row} icon={<Columns3 size={15} aria-hidden="true" />} disabled={!canEdit} />
        <PaletteAction label="普通表格" onClick={actions.grid} icon={<Table2 size={15} aria-hidden="true" />} disabled={!canEdit} />
        <PaletteAction label="条件块" onClick={actions.conditional} icon={<ListFilter size={15} aria-hidden="true" />} disabled={!canEdit} />
        {actions.detailTable ? <PaletteAction label="明细表（自动重复）" onClick={actions.detailTable} icon={<Table2 size={15} aria-hidden="true" />} disabled={!canEdit} /> : null}
      </PaletteSection>
      <PaletteSection title="打印">
        <PaletteAction label="分页符" onClick={actions.pageBreak} icon={<FilePlus2 size={15} aria-hidden="true" />} disabled={!canEdit} />
      </PaletteSection>
      <div className="report-designer-v3-help">先选中元素，再在右侧输入精确坐标。页面始终是 A4，横竖版切换会自动限制元素在页面内。</div>
      <div className="report-designer-v3-report-type">当前数据域：{reportType === "PaymentVoucher" ? "付款/报销" : "出口单据"}</div>
    </div>
  );
}

function PaletteSection({ title, children }: { title: string; children: ReactNode }) {
  return <section className="report-designer-v3-palette-section"><h3>{title}</h3><div className="report-designer-v3-palette-grid">{children}</div></section>;
}

function PaletteAction({ label, icon, onClick, disabled = false }: { label: string; icon: ReactNode; onClick: () => void; disabled?: boolean }) {
  return <button className="report-designer-v3-palette-action" type="button" disabled={disabled} onClick={onClick}>{icon}<span>{label}</span></button>;
}

export function FieldPanel({
  query,
  groups,
  onQueryChange,
  onInsert,
  focusRequest = 0,
  canEdit = true,
}: {
  query: string;
  groups: ReportDesignerFieldGroup[];
  onQueryChange: (value: string) => void;
  onInsert: (field: { label: string; value: string }) => void;
  focusRequest?: number;
  canEdit?: boolean;
}) {
  const searchRef = useRef<HTMLInputElement>(null);
  const fieldCount = groups.reduce((count, group) => count + group.fields.length, 0);
  useEffect(() => {
    if (focusRequest > 0) requestAnimationFrame(() => searchRef.current?.focus());
  }, [focusRequest]);
  return (
    <div className="report-designer-v3-panel-content">
      <div className="report-designer-v3-panel-caption">
        <Pilcrow size={15} aria-hidden="true" />
        <span>选择字段</span>
        <small>{fieldCount} 个可用字段</small>
      </div>
      <label className="report-designer-v3-field-search">
        <span>搜索字段</span>
        <input ref={searchRef} aria-label="搜索字段" value={query} placeholder="发票号、客户、金额..." onChange={(event) => onQueryChange(event.target.value)} />
      </label>
      {groups.length === 0 ? <p className="report-designer-v3-muted">暂无可用字段</p> : groups.map((group) => (
        <details key={group.category} open={Boolean(query.trim()) || groups.length <= 4}>
          <summary>{group.category}<small>{group.fields.length}</small></summary>
          <div className="report-designer-v3-field-list">
            {group.fields.map((field) => (
               <button type="button" key={field.value} disabled={!canEdit} title={`插入 ${field.label}（${field.value}）`} aria-label={`插入字段 ${field.label}`} onClick={() => onInsert(field)}>
                <span>{field.label}</span>
                <small>{field.value}</small>
              </button>
            ))}
          </div>
        </details>
      ))}
    </div>
  );
}

export function LayerPanel({ state, onSelect, onCommit, canEdit = true }: { state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit?: boolean }) {
  return (
    <div className="report-designer-v3-panel-content report-designer-v3-layer-list">
      <div className="report-designer-v3-panel-caption"><Layers3 size={15} aria-hidden="true" /><span>图层与元素</span></div>
      <p className="report-designer-v3-layer-help">点击图层可定位画布；选择元素后可在右侧精确编辑。眼睛和锁定状态会同步到输出。</p>
      {state.schema.layers.map((layer) => <LayerRow key={layer.id} layer={layer} state={state} onSelect={onSelect} onCommit={onCommit} canEdit={canEdit} />)}
    </div>
  );
}

function LayerRow({ layer, state, onSelect, onCommit, canEdit }: { layer: ReportDesignerV3Layer; state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit: boolean }) {
  const [elementsOpen, setElementsOpen] = useState(layer.elements.length <= 12);
  const selectedCount = layer.elements.filter((element) => state.selectedIds.includes(element.id)).length;
  return (
    <section className={`report-designer-v3-layer-row${state.activeLayerId === layer.id ? " is-active" : ""}${layer.visible ? "" : " is-hidden"}`} aria-label={`${layer.name}图层`}>
      <div className="report-designer-v3-layer-heading">
        <button className="report-designer-v3-layer-name" type="button" aria-pressed={state.activeLayerId === layer.id} title={`${layer.name}：${layerPurposes[layer.role]}`} onClick={() => onSelect(layer.id)}>
          <span>{layer.name}</span>
          <small>{layerPurposes[layer.role]} · {layer.elements.length} 个元素{selectedCount ? ` · 已选 ${selectedCount}` : ""}</small>
        </button>
        <button className="report-designer-v3-icon-button report-designer-v3-focus-button" type="button" title="定位到画布" aria-label={`定位${layer.name}图层`} onClick={() => onSelect(layer.id)}>定位</button>
        <button className="report-designer-v3-icon-button" type="button" disabled={!canEdit} title={layer.visible ? "隐藏图层" : "显示图层"} aria-label={layer.visible ? "隐藏图层" : "显示图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { visible: !layer.visible }))}>
          {layer.visible ? <Eye size={15} aria-hidden="true" /> : <EyeOff size={15} aria-hidden="true" />}
        </button>
        <button className="report-designer-v3-icon-button" type="button" disabled={!canEdit} title={layer.locked ? "解锁图层" : "锁定图层"} aria-label={layer.locked ? "解锁图层" : "锁定图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { locked: !layer.locked }))}>
          {layer.locked ? <Lock size={15} aria-hidden="true" /> : <Unlock size={15} aria-hidden="true" />}
        </button>
      </div>
      <LayerDesignControls layer={layer} state={state} onCommit={onCommit} canEdit={canEdit} />
      <LayerPrintControls layer={layer} state={state} onCommit={onCommit} canEdit={canEdit} />
      {layer.elements.length ? (
        <details className="report-designer-v3-layer-elements-disclosure" open={elementsOpen} onToggle={(event) => setElementsOpen(event.currentTarget.open)}>
          <summary>元素列表 <small>{selectedCount ? `已选 ${selectedCount}` : "点击定位"}</small></summary>
          <div className="report-designer-v3-layer-elements">
            {[...layer.elements].sort((left, right) => right.zIndex - left.zIndex).map((element) => (
              <button className={state.selectedIds.includes(element.id) ? "is-selected" : ""} type="button" key={element.id} onClick={() => {
                onCommit({ ...state, selectedIds: [element.id], activeLayerId: layer.id });
                focusDesignerNode(`[data-v3-element-id="${CSS.escape(element.id)}"]`);
              }}>
                <span><strong>{element.type}</strong> {reportDesignerV3ElementText(element) || element.type}</span>
                <small>{element.locked ? "已锁定" : `${hundredthMmToMm(element.xHundredthMm).toFixed(1)}, ${hundredthMmToMm(element.yHundredthMm).toFixed(1)} mm`}</small>
              </button>
            ))}
          </div>
        </details>
      ) : <p className="report-designer-v3-muted">空图层</p>}
    </section>
  );
}

function LayerDesignControls({ layer, state, onCommit, canEdit }: { layer: ReportDesignerV3Layer; state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit: boolean }) {
  const bands = resolveReportDesignerLayerBands(state.schema);
  if (!layer.visible) return <div className="report-designer-v3-layer-design-status">已隐藏 · 不占画布且不输出</div>;
  if (layer.role === "Body") return <div className="report-designer-v3-layer-design-status">设计区高度：{hundredthMmToMm(bands.bodyHeight).toFixed(1)} mm（自动）</div>;
  if (layer.role === "Overlay") return <div className="report-designer-v3-layer-design-status">设计区：整页覆盖</div>;
  return <div className="report-designer-v3-layer-design-fields"><NumberField label="设计区高度 (mm)" value={hundredthMmToMm(reportDesignerLayerHeight(layer))} min={0} max={state.schema.page.heightHundredthMm / 100} disabled={!canEdit} onCommit={(value) => onCommit(setReportDesignerLayerHeight(state, layer.id, Math.round(value * 100)))} /><small>也可拖动画布分隔线</small></div>;
}

function LayerPrintControls({ layer, state, onCommit, canEdit }: { layer: ReportDesignerV3Layer; state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit: boolean }) {
  const print = layer.print;
  const patch = (update: Partial<typeof print>) => onCommit(updateV3Layer(state, layer.id, { print: { ...print, ...update } }));
  return (
    <details className="report-designer-v3-layer-print">
      <summary>打印行为</summary>
      <div className="report-designer-v3-layer-print-fields">
        <CheckRow checked={print.repeatOnEveryPage} disabled={!canEdit || layer.role === "Body"} onChange={(checked) => patch({ repeatOnEveryPage: checked })}>每页重复{layer.role === "Body" ? "（主体不支持）" : ""}</CheckRow>
        <CheckRow checked={print.keepTogether} disabled={!canEdit} onChange={(checked) => patch({ keepTogether: checked })}>保持图层完整</CheckRow>
        <CheckRow checked={print.pinToPageBottom} disabled={!canEdit || layer.role !== "Footer"} onChange={(checked) => patch({ pinToPageBottom: checked })}>页脚贴底{layer.role !== "Footer" ? "（仅页脚）" : ""}</CheckRow>
        <NumberField label="最小高度 (mm)" value={hundredthMmToMm(print.minHeightHundredthMm)} min={0} max={260} disabled={!canEdit} onCommit={(value) => patch({ minHeightHundredthMm: Math.round(value * 100) })} />
      </div>
    </details>
  );
}

export function PageInspector({ state, onCommit, canEdit = true }: { state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit?: boolean }) {
  const page = state.schema.page;
  const margins: Array<[string, keyof Pick<typeof page, "marginTopHundredthMm" | "marginRightHundredthMm" | "marginBottomHundredthMm" | "marginLeftHundredthMm">]> = [
    ["上边距", "marginTopHundredthMm"],
    ["右边距", "marginRightHundredthMm"],
    ["下边距", "marginBottomHundredthMm"],
    ["左边距", "marginLeftHundredthMm"],
  ];
  return (
    <div className="report-designer-v3-inspector-content">
      <InspectorTitle title="页面设置" subtitle="固定 A4 画布" />
      <div className="report-designer-v3-orientation-control">
        <span>方向</span>
        <div>
          <button className={page.orientation === "Portrait" ? "is-active" : ""} type="button" disabled={!canEdit} onClick={() => onCommit(updateV3Page(state, { orientation: "Portrait" }))}>竖版</button>
          <button className={page.orientation === "Landscape" ? "is-active" : ""} type="button" disabled={!canEdit} onClick={() => onCommit(updateV3Page(state, { orientation: "Landscape" }))}>横版</button>
        </div>
      </div>
      <div className="report-designer-v3-page-size-readout"><strong>A4</strong><span>{page.orientation === "Landscape" ? "297 × 210 mm" : "210 × 297 mm"}</span></div>
      <div className="report-designer-v3-inspector-grid">
        {margins.map(([label, key]) => <NumberField key={key} label={label} value={hundredthMmToMm(page[key])} disabled={!canEdit} onCommit={(value) => onCommit(updateV3Page(state, { [key]: Math.round(value * 100) } as never))} />)}
        <NumberField label="网格间距" value={hundredthMmToMm(state.schema.grid.sizeHundredthMm)} min={1} max={50} disabled={!canEdit} onCommit={(value) => onCommit(updateV3Grid(state, { sizeHundredthMm: Math.max(100, Math.round(value * 100)) }))} />
      </div>
      <CheckRow checked={state.schema.grid.enabled} disabled={!canEdit} onChange={(checked) => onCommit(updateV3Grid(state, { enabled: checked }))}>显示网格</CheckRow>
      <CheckRow checked={state.schema.grid.snap} disabled={!canEdit} onChange={(checked) => onCommit(updateV3Grid(state, { snap: checked }))}>拖动时吸附网格</CheckRow>
      <div className="report-designer-v3-inspector-tip">页眉、主体、页脚和覆盖层分别位于独立图层；锁定后仍可预览和输出，但不会被误移动。</div>
    </div>
  );
}

export function MultiElementInspector({
  state,
  onCommit,
  onAlign,
  onDistribute,
  onDuplicate,
  onDelete,
  canEdit = true,
}: {
  state: ReportDesignerV3DocumentState;
  onCommit: (next: ReportDesignerV3DocumentState) => void;
  onAlign: (alignment: "left" | "right" | "top" | "bottom" | "center-horizontal" | "center-vertical") => void;
  onDistribute: (direction: "horizontal" | "vertical") => void;
  onDuplicate: () => void;
  onDelete: () => void;
  canEdit?: boolean;
}) {
  const selectedCount = state.selectedIds.length;
  const selectedElements = state.selectedIds
    .map((id) => findV3Element(state.schema, id))
    .filter((located): located is LocatedElement => located !== null);
  const allLocked = selectedElements.length > 0 && selectedElements.every((loc) => loc.element.locked || loc.layer.locked);
  const allVisible = selectedElements.length > 0 && selectedElements.every((loc) => loc.element.visible);
  const allOutput = selectedElements.length > 0 && selectedElements.every((loc) => loc.element.outputEnabled);
  function batchSetProperty(patch: Partial<ReportDesignerV3Element>) {
    let nextState = state;
    for (const loc of selectedElements) {
      if (!loc.element.locked && !loc.layer.locked) {
        nextState = updateV3Element(nextState, loc.element.id, patch);
      }
    }
    onCommit(nextState);
  }
  function batchSetLocked(locked: boolean) {
    let nextState = state;
    for (const loc of selectedElements) {
      if (!loc.layer.locked) {
        nextState = updateV3Element(nextState, loc.element.id, { locked });
      }
    }
    onCommit(nextState);
  }
  return (
    <div className="report-designer-v3-inspector-content">
      <InspectorTitle title="多选属性" subtitle={`已选中 ${selectedCount} 个元素`} />
      <div className="report-designer-v3-element-type-badge">批量操作</div>
      <div className="report-designer-v3-multi-section">
        <strong>对齐排列</strong>
        <div className="report-designer-v3-element-actions">
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("left")} title="左对齐">左对齐</button>
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("center-horizontal")} title="水平居中">水平居中</button>
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("right")} title="右对齐">右对齐</button>
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("top")} title="顶端对齐">顶端对齐</button>
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("center-vertical")} title="垂直居中">垂直居中</button>
          <button type="button" disabled={!canEdit || selectedCount < 2} onClick={() => onAlign("bottom")} title="底端对齐">底端对齐</button>
        </div>
      </div>
      <div className="report-designer-v3-multi-section">
        <strong>间距分布</strong>
        <div className="report-designer-v3-element-actions">
          <button type="button" disabled={!canEdit || selectedCount < 3} onClick={() => onDistribute("horizontal")} title="3个及以上元素水平分布">水平等距</button>
          <button type="button" disabled={!canEdit || selectedCount < 3} onClick={() => onDistribute("vertical")} title="3个及以上元素垂直分布">垂直等距</button>
        </div>
      </div>
      <div className="report-designer-v3-multi-section">
        <strong>批量操作</strong>
        <div className="report-designer-v3-element-actions">
          <button type="button" disabled={!canEdit} onClick={onDuplicate}>复制所选</button>
          <button type="button" className="is-danger" disabled={!canEdit} onClick={onDelete}>删除所选</button>
        </div>
      </div>
      <div className="report-designer-v3-multi-section">
        <strong>批量属性</strong>
        <CheckRow checked={allVisible} disabled={!canEdit} onChange={(checked) => batchSetProperty({ visible: checked })}>全部在画布显示</CheckRow>
        <CheckRow checked={allOutput} disabled={!canEdit} onChange={(checked) => batchSetProperty({ outputEnabled: checked })}>全部参与打印输出</CheckRow>
        <CheckRow checked={allLocked} disabled={!canEdit} onChange={(checked) => batchSetLocked(checked)}>全部锁定</CheckRow>
      </div>
      <details className="report-designer-v3-layer-elements-disclosure" open>
        <summary>已选元素列表 <small>{selectedCount} 个</small></summary>
        <div className="report-designer-v3-layer-elements">
          {selectedElements.map(({ element, layer }) => (
            <button
              key={element.id}
              type="button"
              className="is-selected"
              onClick={() => {
                onCommit({ ...state, selectedIds: [element.id], activeLayerId: layer.id });
                focusDesignerNode(`[data-v3-element-id="${CSS.escape(element.id)}"]`);
              }}
              title="点击单独选中此元素"
            >
              <span><strong>{element.type}</strong> {reportDesignerV3ElementText(element) || element.type}</span>
              <small>{hundredthMmToMm(element.xHundredthMm).toFixed(1)}, {hundredthMmToMm(element.yHundredthMm).toFixed(1)} mm</small>
            </button>
          ))}
        </div>
      </details>
    </div>
  );
}

export function ElementInspector({
  located,
  fieldGroups,
  state,
  onPatch,
  onPatchStyle,
  onCommit,
  onFlowCommit,
  selectedGridCellId,
  onSelectGridCell,
  onZIndex,
  canEdit = true,
  client,
  onImageResourceUploaded,
}: {
  located: LocatedElement;
  fieldGroups: ReportDesignerFieldGroup[];
  state: ReportDesignerV3DocumentState;
  onPatch: (update: Partial<ReportDesignerV3Element>) => void;
  onPatchStyle: (update: Partial<ReportDesignerV3Element["style"]>) => void;
  onCommit: (next: ReportDesignerV3DocumentState) => void;
  onFlowCommit: (block: FlowBlock) => void;
  selectedGridCellId?: string;
  onSelectGridCell: (cellId: string) => void;
  onZIndex: (direction: "front" | "back" | "forward" | "backward") => void;
  canEdit?: boolean;
  client?: ExportDocManagerApiClient;
  onImageResourceUploaded: (elementId: string, resource: ApiReportTemplateImageResourceResponse) => void;
}) {
  const { element, layer } = located;
  const editable = canEdit && !element.locked && !layer.locked;
  return (
    <div className="report-designer-v3-inspector-content">
      <InspectorTitle title="元素属性" subtitle={reportDesignerV3ElementText(element) || element.type} />
      <div className="report-designer-v3-element-type-badge">{element.type}{element.type === "Flow" ? ` · ${element.flowKind}` : ""}</div>
      <div className="report-designer-v3-inspector-grid">
        <NumberField label="X (mm)" value={hundredthMmToMm(element.xHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ xHundredthMm: Math.round(value * 100) })} />
        <NumberField label="Y (mm)" value={hundredthMmToMm(element.yHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ yHundredthMm: Math.round(value * 100) })} />
        <NumberField label="宽 (mm)" value={hundredthMmToMm(element.widthHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ widthHundredthMm: Math.round(value * 100) })} />
        <NumberField label="高 (mm)" value={hundredthMmToMm(element.heightHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ heightHundredthMm: Math.round(value * 100) })} />
        <NumberField label="旋转角度 (°)" value={element.rotationDeg} min={-360} max={360} disabled={!editable} onCommit={(value) => onPatch({ rotationDeg: Math.round(value * 100) / 100 })} />
      </div>
      <ElementContentEditor element={element} reportType={state.schema.reportType} resources={state.schema.resources ?? []} fieldGroups={fieldGroups} editable={editable} client={client} onPatch={onPatch} onFlowCommit={onFlowCommit} selectedGridCellId={selectedGridCellId} onSelectGridCell={onSelectGridCell} onImageResourceUploaded={onImageResourceUploaded} />
      {element.type !== "Flow" ? <ElementStyleEditor style={element.style} editable={editable} onPatch={onPatchStyle} /> : null}
      <div className="report-designer-v3-element-actions">
        {(["back", "backward", "forward", "front"] as const).map((direction) => <button key={direction} type="button" onClick={() => onZIndex(direction)} disabled={!editable}>{direction === "back" ? "置底" : direction === "backward" ? "后移" : direction === "forward" ? "前移" : "置顶"}</button>)}
      </div>
      <CheckRow checked={element.visible} disabled={!canEdit} onChange={(checked) => onCommit(updateV3Element(state, element.id, { visible: checked }))}>在画布中显示</CheckRow>
      <CheckRow checked={element.outputEnabled} disabled={!canEdit} onChange={(checked) => onCommit(updateV3Element(state, element.id, { outputEnabled: checked }))}>参与打印输出</CheckRow>
      <CheckRow checked={element.locked} disabled={!canEdit || layer.locked} onChange={(checked) => onCommit(updateV3Element(state, element.id, { locked: checked }))}>锁定元素</CheckRow>
      {layer.locked ? <div className="report-designer-v3-lock-note"><Lock size={14} aria-hidden="true" />图层已锁定，请先在图层面板解锁。</div> : null}
    </div>
  );
}

function ElementContentEditor({ element, reportType, resources, fieldGroups, editable, client, onPatch, onFlowCommit, selectedGridCellId, onSelectGridCell, onImageResourceUploaded }: { element: ReportDesignerV3Element; reportType: ReportDesignerReportType; resources: ReportDesignerV3ImageResource[]; fieldGroups: ReportDesignerFieldGroup[]; editable: boolean; client?: ExportDocManagerApiClient; onPatch: (update: Partial<ReportDesignerV3Element>) => void; onFlowCommit: (block: FlowBlock) => void; selectedGridCellId?: string; onSelectGridCell: (cellId: string) => void; onImageResourceUploaded: (elementId: string, resource: ApiReportTemplateImageResourceResponse) => void }) {
  switch (element.type) {
    case "Text":
      return <label className="report-designer-v3-wide-field"><span>文本</span><CommitTextField value={element.text} multiline disabled={!editable} onCommit={(text) => onPatch({ text })} /></label>;
    case "Field": {
      const options = [
        { value: "", label: "请选择字段" },
        ...flattenFields(fieldGroups).map((field) => ({ value: field.value, label: `${field.label} · ${field.value}` })),
      ];
      return <><SelectField label="字段" value={element.fieldPath} options={options} disabled={!editable} onChange={(fieldPath) => onPatch({ fieldPath })} /><label><span>占位文本</span><CommitTextField value={element.fallbackText ?? ""} disabled={!editable} onCommit={(fallbackText) => onPatch({ fallbackText: fallbackText || undefined })} /></label></>;
    }
    case "Image": {
      return <ImageSourceEditor element={element} reportType={reportType} resources={resources} editable={editable} client={client} onPatch={onPatch} onUploaded={onImageResourceUploaded} />;
    }
    case "PageNumber":
      return <><SelectField label="页码格式" value={element.format} options={[{ value: "CurrentOfTotal", label: "当前页 / 总页数" }, { value: "Current", label: "当前页" }]} disabled={!editable} onChange={(format) => onPatch({ format: format === "Current" ? "Current" : "CurrentOfTotal" })} /><label><span>前缀</span><CommitTextField value={element.prefix ?? ""} disabled={!editable} placeholder="例如：第 " onCommit={(prefix) => onPatch({ prefix: prefix || undefined })} /></label><label><span>后缀</span><CommitTextField value={element.suffix ?? ""} disabled={!editable} placeholder="例如： 页" onCommit={(suffix) => onPatch({ suffix: suffix || undefined })} /></label></>;
    case "Line":
      return <SelectField label="方向" value={element.direction} options={[{ value: "Horizontal", label: "水平" }, { value: "Vertical", label: "垂直" }]} disabled={!editable} onChange={(direction) => onPatch({ direction: direction === "Vertical" ? "Vertical" : "Horizontal" })} />;
    case "Flow":
      return <><div className="report-designer-v3-inspector-tip">结构化业务组件的内容和样式在下方统一编辑；普通表格也可直接点击画布单元格切换选区。</div><fieldset className="report-designer-v3-flow-editor" disabled={!editable}><ReportDesignerV3FlowProperties block={element.block} fieldGroups={fieldGroups} selectedGridCellId={selectedGridCellId} onSelectGridCell={onSelectGridCell} onCommit={onFlowCommit} /></fieldset></>;
    case "Rectangle":
      return null;
  }
}

function ImageSourceEditor({ element, reportType, resources, editable, client, onPatch, onUploaded }: { element: Extract<ReportDesignerV3Element, { type: "Image" }>; reportType: ReportDesignerReportType; resources: ReportDesignerV3ImageResource[]; editable: boolean; client?: ExportDocManagerApiClient; onPatch: (update: Partial<ReportDesignerV3Element>) => void; onUploaded: (elementId: string, resource: ApiReportTemplateImageResourceResponse) => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [feedback, setFeedback] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const imageFields = getControlledReportImageFieldPaths(reportType);
  const currentImageField = isControlledReportImageFieldPath(element.fieldPath) ? element.fieldPath : "";
  const imageFieldOptions = [
    { value: "", label: "请选择受控图片字段" },
    ...imageFields.map((fieldPath) => ({ value: fieldPath, label: fieldPath })),
  ];
  const resourceOptions = [
    { value: "", label: resources.length ? "请选择已上传图片" : "暂无已上传图片" },
    ...resources.map((resource) => ({
      value: resource.id,
      label: `${resource.altText || "图片"} · ${formatResourceSize(resource.byteLength)} · ${resource.id.slice(0, 16)}…`,
    })),
  ];

  async function upload(file: File) {
    if (!client || !editable) return;
    if (file.size > 32 * 1024 * 1024) {
      setFeedback({ tone: "error", text: "图片不能超过 32 MB。" });
      return;
    }
    setUploading(true);
    setFeedback(null);
    try {
      const resource = await client.uploadReportTemplateV3ImageResource({
        fileName: file.name,
        mediaType: file.type || undefined,
        body: file,
      });
      onUploaded(element.id, resource);
      setFeedback({ tone: "success", text: "图片已上传并自动绑定，无需填写资源 ID。" });
    } catch (error) {
      setFeedback({ tone: "error", text: readApiError(error) });
    } finally {
      setUploading(false);
    }
  }

  return (
    <div className="report-designer-v3-image-editor">
      <SelectField label="来源" value={element.sourceKind} options={[{ value: "Field", label: "字段图片" }, { value: "Resource", label: "上传图片" }]} disabled={!editable} onChange={(value) => {
        const sourceKind = value === "Resource" ? "Resource" : "Field";
        onPatch(sourceKind === "Field"
          ? { sourceKind, fieldPath: currentImageField || imageFields[0], resourceId: undefined }
          : { sourceKind, fieldPath: undefined, resourceId: element.resourceId || resources[0]?.id });
      }} />
      {element.sourceKind === "Field" ? (
        <>
          <SelectField label="图片字段" value={element.fieldPath ?? ""} options={imageFieldOptions} disabled={!editable || imageFields.length === 0} onChange={(fieldPath) => onPatch({ fieldPath: fieldPath || undefined })} />
          {imageFields.length === 0 ? <small className="report-designer-v3-muted">当前报表类型没有可绑定的受控图片字段。</small> : null}
        </>
      ) : (
        <div className="report-designer-v3-resource-picker">
          <SelectField label="已上传图片" value={element.resourceId ?? ""} options={resourceOptions} disabled={!editable || resources.length === 0} onChange={(resourceId) => onPatch({ resourceId: resourceId || undefined })} />
          <input ref={inputRef} type="file" hidden accept="image/png,image/jpeg,image/gif,image/webp,.png,.jpg,.jpeg,.gif,.webp" onChange={(event) => {
            const file = event.currentTarget.files?.[0];
            event.currentTarget.value = "";
            if (file) void upload(file);
          }} />
          <button className="command-button secondary report-designer-v3-upload-button" type="button" disabled={!editable || uploading || !client} onClick={() => inputRef.current?.click()}>
            <Upload size={15} aria-hidden="true" />
            <span>{uploading ? "正在上传…" : "选择图片并上传"}</span>
          </button>
          <small className="report-designer-v3-resource-help">支持 PNG、JPEG、GIF、WebP，最大 32 MB；上传后自动生成并绑定受控资源。</small>
          {element.resourceId ? <div className="report-designer-v3-resource-id"><span>资源 ID</span><code>{element.resourceId}</code></div> : null}
          {feedback ? <div className={`report-designer-v3-upload-feedback is-${feedback.tone}`} role={feedback.tone === "error" ? "alert" : "status"}>{feedback.text}</div> : null}
        </div>
      )}
      <label><span>替代文本</span><CommitTextField value={element.altText ?? ""} disabled={!editable} placeholder="例如：公司标志" onCommit={(altText) => onPatch({ altText: altText || undefined })} /></label>
      <CheckRow checked={element.hideWhenSourceEmpty} disabled={!editable} onChange={(hideWhenSourceEmpty) => onPatch({ hideWhenSourceEmpty })}>来源为空时隐藏</CheckRow>
    </div>
  );
}

function formatResourceSize(value?: number) {
  if (!value || value <= 0) return "未知大小";
  return value >= 1024 * 1024 ? `${(value / (1024 * 1024)).toFixed(1)} MB` : `${Math.ceil(value / 1024)} KB`;
}

function ElementStyleEditor({ style, editable, onPatch }: { style: ReportDesignerV3ElementStyle; editable: boolean; onPatch: (update: Partial<ReportDesignerV3ElementStyle>) => void }) {
  return <div className="report-designer-v3-style-editor"><strong>样式</strong><div className="report-designer-v3-inspector-grid"><NumberField label="字号 pt" value={style.fontSizePt ?? 10} min={6} max={96} disabled={!editable} onCommit={(fontSizePt) => onPatch({ fontSizePt })} /><SelectField label="对齐" value={style.align ?? "Left"} options={[{ value: "Left", label: "左" }, { value: "Center", label: "中" }, { value: "Right", label: "右" }]} disabled={!editable} onChange={(align) => onPatch({ align: align as "Left" | "Center" | "Right" })} /></div><CheckRow checked={style.bold === true} disabled={!editable} onChange={(bold) => onPatch({ bold })}>粗体</CheckRow><ReportDesignerV3ColorField label="文字颜色" value={style.color ?? "#1f2933"} disabled={!editable} onCommit={(color) => onPatch({ color })} /><ReportDesignerV3ColorField label="背景颜色" value={style.backgroundColor ?? ""} allowEmpty disabled={!editable} onCommit={(backgroundColor) => onPatch({ backgroundColor: backgroundColor || undefined })} /></div>;
}

function SelectField({ label, value, options, disabled = false, className, onChange }: { label: string; value: string; options: SelectOption[]; disabled?: boolean; className?: string; onChange: (value: string) => void }) {
  const safeOptions = ensureCurrentSelectOption(options, value);
  const hasUnknownValue = Boolean(value) && !options.some((option) => option.value === value);
  return <label className={className}><span>{label}</span><select aria-invalid={hasUnknownValue ? true : undefined} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>{safeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>;
}

function InspectorTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return <div className="report-designer-v3-inspector-title"><strong>{title}</strong><span>{subtitle}</span></div>;
}

function CheckRow({ checked, disabled = false, onChange, children }: { checked: boolean; disabled?: boolean; onChange: (checked: boolean) => void; children: ReactNode }) {
  return <label className="report-designer-v3-check-row"><input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{children}</span></label>;
}

function NumberField({ label, value, onCommit, min = 0, max = 1000, disabled = false }: { label: string; value: number; onCommit: (value: number) => void; min?: number; max?: number; disabled?: boolean }) {
  const [draft, setDraft] = useState(formatNumber(value));
  const cancelOnBlur = useRef(false);
  useEffect(() => setDraft(formatNumber(value)), [value]);
  function commit() {
    if (cancelOnBlur.current) {
      cancelOnBlur.current = false;
      setDraft(formatNumber(value));
      return;
    }
    const parsed = Number(draft);
    if (!Number.isFinite(parsed)) return setDraft(formatNumber(value));
    const next = Math.min(max, Math.max(min, parsed));
    setDraft(formatNumber(next));
    if (next !== value) onCommit(next);
  }
  return <label><span>{label}</span><input type="number" inputMode="decimal" step="0.01" min={min} max={max} disabled={disabled} value={draft} onChange={(event) => setDraft(event.target.value)} onBlur={commit} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); cancelOnBlur.current = true; setDraft(formatNumber(value)); event.currentTarget.blur(); } else if (event.key === "Enter") { event.preventDefault(); event.currentTarget.blur(); } }} /></label>;
}

export function focusDesignerNode(selector: string) {
  requestAnimationFrame(() => document.querySelector<HTMLElement>(selector)?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" }));
}
