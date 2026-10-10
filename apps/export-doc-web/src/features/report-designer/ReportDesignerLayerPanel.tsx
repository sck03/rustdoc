import { useEffect, useMemo, useState } from "react";
import { Eye, EyeOff, Layers3, Lock, Unlock } from "lucide-react";
import { CommitTextField, DesignerCheckbox as CheckRow } from "./ReportDesignerPropertyControls.tsx";
import { NumberField, SelectField, focusDesignerNode } from "./ReportDesignerV3InspectorControls.tsx";
import { reportDesignerLayerHeight, resolveReportDesignerLayerBands, setReportDesignerLayerHeight } from "./reportDesignerLayerBands.ts";
import { updateV3Layer, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { hundredthMmToMm, reportDesignerV3ElementText, reportDesignerV3ElementKindLabel, REPORT_DESIGNER_V3_MAX_LAYER_COUNT, type ReportDesignerV3Layer, type ReportDesignerV3LayerRole } from "./reportDesignerV3Schema.ts";
import { addV3Layer, getV3LayerRemovalIssue, moveV3Layer, removeV3Layer } from "./reportDesignerV3Layers.ts";
import { v3RegionNames } from "./reportDesignerV3Regions.ts";

const layerPurposes: Record<ReportDesignerV3Layer["role"], string> = {
  Header: "页眉 · 可调高度/每页重复",
  Body: "主体 · 自动填充剩余页面",
  Footer: "页脚 · 可调高度/贴底",
  Overlay: "覆盖层 · 水印/印章",
};

export function LayerPanel({ state, onSelect, onCommit, canEdit = true, multiSelect = false, hidden = false }: { state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit?: boolean; multiSelect?: boolean; hidden?: boolean }) {
  const [query, setQuery] = useState("");
  const [newRole, setNewRole] = useState<ReportDesignerV3LayerRole>("Footer");
  const bands = useMemo(() => resolveReportDesignerLayerBands(state.schema), [state.schema]);
  const search = query.trim().toLocaleLowerCase();
  return (
    <div className="report-designer-v3-panel-content report-designer-v3-layer-list" hidden={hidden}>
      <div className="report-designer-v3-panel-caption"><Layers3 size={15} aria-hidden="true" /><span>图层与元素</span></div>
      <label className="report-designer-v3-field-search"><span>搜索已放置的内容</span><input aria-label="搜索已放置的内容" value={query} onChange={event => setQuery(event.target.value)} placeholder="标题、货名、金额…" /></label>
      <p className="report-designer-v3-layer-help">{multiSelect ? "多选已开启：点击列表逐项选择，再统一对齐或调整大小。" : "点击列表定位内容；打开工具栏“多选”可批量排版。隐藏图层不输出，锁定防止误改。"}</p>
      <details className="report-designer-layer-settings">
        <summary>新增图层 <small>{state.schema.layers.length} / {REPORT_DESIGNER_V3_MAX_LAYER_COUNT}</small></summary>
        <SelectField label="新增图层类型" value={newRole} options={Object.entries(v3RegionNames).map(([value, label]) => ({ value, label }))} disabled={!canEdit} onChange={value => setNewRole(value as ReportDesignerV3LayerRole)} />
        <button type="button" className="command-button secondary" disabled={!canEdit || state.schema.layers.length >= REPORT_DESIGNER_V3_MAX_LAYER_COUNT} onClick={() => { setQuery(""); onCommit(addV3Layer(state, newRole)); }}>添加图层</button>
        <small>同类图层可独立设置首页、每页或末页输出，用于唛头、合计、条款与签章。</small>
      </details>
      {state.schema.layers.map((layer, index) => <LayerRow key={layer.id} layer={layer} index={index} bodyHeight={bands.bodyHeight} state={state} onSelect={onSelect} onCommit={onCommit} canEdit={canEdit} search={search} multiSelect={multiSelect} />)}
    </div>
  );
}

function LayerRow({ layer, index, bodyHeight, state, onSelect, onCommit, canEdit, search, multiSelect }: { layer: ReportDesignerV3Layer; index: number; bodyHeight: number; state: ReportDesignerV3DocumentState; onSelect: (id: string) => void; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit: boolean; search: string; multiSelect: boolean }) {
  const active = state.activeLayerId === layer.id;
  const [elementsOpen, setElementsOpen] = useState(active);
  useEffect(() => { if (active) setElementsOpen(true); }, [active]);
  const selectedCount = layer.elements.filter((element) => state.selectedIds.includes(element.id)).length;
  const elements = layer.elements.filter(element => !search || `${element.label ?? ""} ${reportDesignerV3ElementText(element)} ${reportDesignerV3ElementKindLabel(element)} ${element.type === "Field" ? element.fieldPath : ""}`.toLocaleLowerCase().includes(search));
  const removalIssue = getV3LayerRemovalIssue(state, layer.id);
  if (search && !elements.length) return null;
  return (
    <section className={`report-designer-v3-layer-row${state.activeLayerId === layer.id ? " is-active" : ""}${layer.visible ? "" : " is-hidden"}`} data-layer-role={layer.role} aria-label={`${layer.name}图层`}>
      <div className="report-designer-v3-layer-heading">
        <button className="report-designer-v3-layer-name" type="button" aria-pressed={state.activeLayerId === layer.id} title={`${layer.name}：${layerPurposes[layer.role]}`} onClick={() => onSelect(layer.id)}>
          <span>{layer.name}</span>
          <small>{layerPurposes[layer.role]} · {layer.elements.length} 个元素{selectedCount ? ` · 已选 ${selectedCount}` : ""}</small>
        </button>
        <button className="report-designer-v3-icon-button" type="button" disabled={!canEdit} title={layer.visible ? "隐藏图层" : "显示图层"} aria-label={layer.visible ? "隐藏图层" : "显示图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { visible: !layer.visible }))}>
          {layer.visible ? <Eye size={15} aria-hidden="true" /> : <EyeOff size={15} aria-hidden="true" />}
        </button>
        <button className="report-designer-v3-icon-button" type="button" disabled={!canEdit} title={layer.locked ? "解锁图层" : "锁定图层"} aria-label={layer.locked ? "解锁图层" : "锁定图层"} onClick={() => onCommit(updateV3Layer(state, layer.id, { locked: !layer.locked }))}>
          {layer.locked ? <Lock size={15} aria-hidden="true" /> : <Unlock size={15} aria-hidden="true" />}
        </button>
      </div>
      {layer.elements.length ? (
        <details className="report-designer-v3-layer-elements-disclosure" open={Boolean(search) || elementsOpen} onToggle={(event) => { if (!search) setElementsOpen(event.currentTarget.open); }}>
          <summary>元素列表 <small>{selectedCount ? `已选 ${selectedCount}` : "点击定位"}</small></summary>
          <div className="report-designer-v3-layer-elements">
            {[...elements].sort((left, right) => right.zIndex - left.zIndex).map((element) => (
              <button className={state.selectedIds.includes(element.id) ? "is-selected" : ""} type="button" key={element.id} aria-pressed={state.selectedIds.includes(element.id)} onClick={(event) => {
                const additive = multiSelect || event.ctrlKey || event.metaKey || event.shiftKey;
                const selectedIds = additive ? state.selectedIds.includes(element.id) ? state.selectedIds.filter(id => id !== element.id) : [...state.selectedIds, element.id] : [element.id];
                onCommit({ ...state, selectedIds, activeLayerId: layer.id });
                if (!additive) focusDesignerNode(`[data-v3-element-id="${CSS.escape(element.id)}"]`);
              }}>
                <span>{element.type !== "Flow" ? <strong>{reportDesignerV3ElementKindLabel(element)} </strong> : null}{reportDesignerV3ElementText(element) || reportDesignerV3ElementKindLabel(element)}</span>
                <small>{!element.visible ? "已隐藏 · " : ""}{element.locked ? "已锁定" : `${hundredthMmToMm(element.xHundredthMm).toFixed(1)}, ${hundredthMmToMm(element.yHundredthMm).toFixed(1)} mm`}</small>
              </button>
            ))}
          </div>
        </details>
      ) : <p className="report-designer-v3-muted">空图层</p>}
      <details className="report-designer-layer-settings"><summary>图层设置</summary>
        <label><span>图层名称</span><CommitTextField value={layer.name} disabled={!canEdit || layer.locked} onCommit={name => onCommit(updateV3Layer(state, layer.id, { name }))} /></label>
        <LayerDesignControls layer={layer} bodyHeight={bodyHeight} state={state} onCommit={onCommit} canEdit={canEdit} />
        <LayerPrintControls layer={layer} state={state} onCommit={onCommit} canEdit={canEdit} />
        <div className="report-designer-v3-element-actions">
          {([-1, 1] as const).map(direction => <button key={direction} type="button" disabled={!canEdit || layer.locked || !state.schema.layers[index + direction] || state.schema.layers[index + direction].locked} onClick={() => onCommit(moveV3Layer(state, layer.id, direction))}>{direction === -1 ? "上移图层" : "下移图层"}</button>)}
          <button type="button" disabled={!canEdit || Boolean(removalIssue)} title={removalIssue ?? "删除空图层，可撤销"} onClick={() => onCommit(removeV3Layer(state, layer.id))}>删除空图层</button>
        </div>
      </details>
    </section>
  );
}

function LayerDesignControls({ layer, bodyHeight, state, onCommit, canEdit }: { layer: ReportDesignerV3Layer; bodyHeight: number; state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void; canEdit: boolean }) {
  if (!layer.visible) return <div className="report-designer-v3-layer-design-status">已隐藏 · 不占画布且不输出</div>;
  if (layer.role === "Body") return <div className="report-designer-v3-layer-design-status">设计区高度：{hundredthMmToMm(bodyHeight).toFixed(1)} mm（自动）</div>;
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
        <CheckRow checked={print.pinToPageBottom} disabled={!canEdit || layer.role !== "Footer"} onChange={(checked) => patch({ pinToPageBottom: checked, followBody: checked ? false : print.followBody })}>页脚贴底{layer.role !== "Footer" ? "（仅页脚）" : ""}</CheckRow>
        <CheckRow checked={print.followBody === true} disabled={!canEdit || layer.role !== "Footer"} onChange={checked => patch({ followBody: checked, pinToPageBottom: checked ? false : print.pinToPageBottom })}>跟随正文（签字区、条款）</CheckRow>
        <CheckRow checked={print.firstPageOnly === true} disabled={!canEdit || layer.role === "Body"} onChange={checked => patch({ firstPageOnly: checked })}>仅首页输出</CheckRow>
        <NumberField label="最小高度 (mm)" value={hundredthMmToMm(print.minHeightHundredthMm)} min={0} max={260} disabled={!canEdit} onCommit={(value) => patch({ minHeightHundredthMm: Math.round(value * 100) })} />
      </div>
    </details>
  );
}
