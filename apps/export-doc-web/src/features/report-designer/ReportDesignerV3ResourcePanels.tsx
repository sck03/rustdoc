import { useEffect, useRef, useState, type ReactNode } from "react";
import { Columns3, Eye, EyeOff, FilePlus2, Hash, Image as ImageIcon, Layers3, ListFilter, Lock, Pilcrow, Table2, Unlock } from "lucide-react";
import { DesignerCheckbox as CheckRow } from "./ReportDesignerPropertyControls.tsx";
import { NumberField, focusDesignerNode } from "./ReportDesignerV3InspectorControls.tsx";
import { reportDesignerLayerHeight, resolveReportDesignerLayerBands, setReportDesignerLayerHeight } from "./reportDesignerLayerBands.ts";
import { updateV3Layer, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { hundredthMmToMm, reportDesignerV3ElementText, reportDesignerV3ElementKindLabel, type ReportDesignerV3Layer } from "./reportDesignerV3Schema.ts";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

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
      <div className="report-designer-v3-help">双击文字或单元格直接编辑，也可选中后按 F2。拖动移动，拖动边角调大小；更多设置在右侧。</div>
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
                <span>{element.type !== "Flow" ? <strong>{reportDesignerV3ElementKindLabel(element)} </strong> : null}{reportDesignerV3ElementText(element) || reportDesignerV3ElementKindLabel(element)}</span>
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
