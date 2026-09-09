import { ImageSourceEditor } from "./ReportDesignerV3ImageProperties.tsx";
import { NumberField, SelectField, InspectorTitle, focusDesignerNode } from "./ReportDesignerV3InspectorControls.tsx";
import { useState } from "react";
import { Lock } from "lucide-react";
import type {
  ApiReportTemplateImageResourceResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { CommitTextField, DesignerCheckbox as CheckRow, DesignerPropertyTabs } from "./ReportDesignerPropertyControls.tsx";
import { moveV3SelectionToRegion, requiresV3BodyRegion, v3RegionNames } from "./reportDesignerV3Regions.ts";
import { ReportDesignerV3ColorField } from "./ReportDesignerV3ColorField.tsx";
import { ReportDesignerV3FlowProperties } from "./ReportDesignerV3FlowProperties.tsx";
import {
  findV3Element,
  alignSelectedV3Elements,
  matchSelectedV3ElementSize,
  updateSelectedV3ElementFlags,
  updateV3Element,
  updateV3Grid,
  updateV3Page,
  type ReportDesignerV3DocumentState,
} from "./reportDesignerV3Mutations.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementText,
  reportDesignerV3ElementKindLabel,
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementStyle,
  type ReportDesignerV3ImageResource,
} from "./reportDesignerV3Schema.ts";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { flattenFields } from "./reportDesignerV3WorkspaceHelpers.tsx";

type FlowBlock = Extract<ReportBlock, { type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak" }>;
type LocatedElement = NonNullable<ReturnType<typeof findV3Element>>;
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
  const movableCount = selectedElements.filter(({ element, layer }) => !element.locked && !layer.locked).length;
  return (
    <div className="report-designer-v3-inspector-content">
      <InspectorTitle title="多选属性" subtitle={`已选中 ${selectedCount} 个元素`} />
      <RegionSelector state={state} onCommit={onCommit} disabled={!canEdit || allLocked} />
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
        <strong>统一尺寸</strong>
        <div className="report-designer-v3-element-actions">
          {(["width", "height", "both"] as const).map((dimension) => <button key={dimension} type="button" disabled={!canEdit || movableCount < 2}
            onClick={() => onCommit(matchSelectedV3ElementSize(state, dimension))}>{dimension === "width" ? "等宽" : dimension === "height" ? "等高" : "相同大小"}</button>)}
        </div>
        <small className="report-designer-v3-muted">以先选中的可编辑元素为基准，锁定元素保留原位。</small>
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
        <CheckRow checked={allVisible} mixed={!allVisible && selectedElements.some(({ element }) => element.visible)} disabled={!canEdit || selectedElements.every(({ layer }) => layer.locked)} onChange={(visible) => onCommit(updateSelectedV3ElementFlags(state, { visible }))}>全部在画布显示</CheckRow>
        <CheckRow checked={allOutput} mixed={!allOutput && selectedElements.some(({ element }) => element.outputEnabled)} disabled={!canEdit || selectedElements.every(({ layer }) => layer.locked)} onChange={(outputEnabled) => onCommit(updateSelectedV3ElementFlags(state, { outputEnabled }))}>全部参与打印输出</CheckRow>
        <CheckRow checked={allLocked} mixed={!allLocked && selectedElements.some(({ element, layer }) => element.locked || layer.locked)} disabled={!canEdit || selectedElements.every(({ layer }) => layer.locked)} onChange={(locked) => onCommit(updateSelectedV3ElementFlags(state, { locked }))}>全部锁定</CheckRow>
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
              <span>{element.type !== "Flow" ? <strong>{reportDesignerV3ElementKindLabel(element)} </strong> : null}{reportDesignerV3ElementText(element) || reportDesignerV3ElementKindLabel(element)}</span>
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
  const [tab, setTab] = useState<"content" | "style" | "layout">(element.type === "Rectangle" || element.type === "Line" ? "style" : "content");
  const tabs = [
    ...(element.type === "Rectangle" ? [] : [{ value: "content" as const, label: "内容" }]),
    { value: "style" as const, label: "外观" }, { value: "layout" as const, label: "布局" },
  ];
  const geometry = <details className="report-designer-property-section" open={element.type !== "Flow"}>
      <summary>位置与大小 <small>{hundredthMmToMm(element.widthHundredthMm)} × {hundredthMmToMm(element.heightHundredthMm)} mm</small></summary>
      <div className="report-designer-v3-inspector-grid">
        <NumberField label="X (mm)" value={hundredthMmToMm(element.xHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ xHundredthMm: Math.round(value * 100) })} />
        <NumberField label="Y (mm)" value={hundredthMmToMm(element.yHundredthMm)} disabled={!editable} onCommit={(value) => onPatch({ yHundredthMm: Math.round(value * 100) })} />
        <NumberField label="宽 (mm)" value={hundredthMmToMm(element.widthHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ widthHundredthMm: Math.round(value * 100) })} />
        <NumberField label="高 (mm)" value={hundredthMmToMm(element.heightHundredthMm)} min={4} disabled={!editable} onCommit={(value) => onPatch({ heightHundredthMm: Math.round(value * 100) })} />
        <NumberField label="旋转角度 (°)" value={element.rotationDeg} min={-360} max={360} disabled={!editable} onCommit={(value) => onPatch({ rotationDeg: Math.round(value * 100) / 100 })} />
      </div>
      <div className="report-designer-v3-element-actions">
        <button type="button" disabled={!editable} onClick={() => onCommit(alignSelectedV3Elements(state, "center-horizontal", "page"))}>页面水平居中</button>
        <button type="button" disabled={!editable} onClick={() => onCommit(alignSelectedV3Elements(state, "center-vertical", "page"))}>页面垂直居中</button>
      </div>
    </details>;
  const content = <ElementContentEditor element={element} reportType={state.schema.reportType} resources={state.schema.resources ?? []} fieldGroups={fieldGroups} editable={editable} client={client} onPatch={onPatch} onFlowCommit={onFlowCommit} selectedGridCellId={selectedGridCellId} onSelectGridCell={onSelectGridCell} onImageResourceUploaded={onImageResourceUploaded} />;
  const output = <details className="report-designer-property-section"><summary>排列与输出</summary>
      <div className="report-designer-v3-element-actions">
        {(["back", "backward", "forward", "front"] as const).map((direction) => <button key={direction} type="button" onClick={() => onZIndex(direction)} disabled={!editable}>{direction === "back" ? "置底" : direction === "backward" ? "后移" : direction === "forward" ? "前移" : "置顶"}</button>)}
      </div>
      <CheckRow checked={element.visible} disabled={!canEdit || layer.locked} onChange={(checked) => onCommit(updateV3Element(state, element.id, { visible: checked }))}>在画布中显示</CheckRow>
      <CheckRow checked={element.outputEnabled} disabled={!canEdit || layer.locked} onChange={(checked) => onCommit(updateV3Element(state, element.id, { outputEnabled: checked }))}>参与打印输出</CheckRow>
      <CheckRow checked={element.locked} disabled={!canEdit || layer.locked} onChange={(checked) => onCommit(updateV3Element(state, element.id, { locked: checked }))}>锁定元素</CheckRow>
    </details>;
  return (
    <div className="report-designer-v3-inspector-content">
      <InspectorTitle title={element.type === "Flow" ? reportDesignerV3ElementText(element) : reportDesignerV3ElementKindLabel(element)} subtitle={`${v3RegionNames[layer.role]}区域${element.locked || layer.locked ? " · 已锁定" : ""}`} />
      {element.type === "Flow" ? <><RegionSelector state={state} onCommit={onCommit} disabled={!editable} />{geometry}{content}{output}</> :
        <DesignerPropertyTabs value={tab} options={tabs} onChange={setTab}>
          {tab === "content" ? content : tab === "style" ? <ElementStyleEditor element={element} editable={editable} onPatch={onPatchStyle} /> :
            <><RegionSelector state={state} onCommit={onCommit} disabled={!editable} />{geometry}{output}</>}
        </DesignerPropertyTabs>}
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
      return <><SelectField label="字段" value={element.fieldPath} options={options} disabled={!editable} onChange={(fieldPath) => onPatch({ fieldPath })} />
        <label><span>字段标签（选填）</span><CommitTextField value={element.label ?? ""} disabled={!editable} onCommit={(label) => onPatch({ label: label || undefined })} /></label>
        <label><span>占位文本</span><CommitTextField value={element.fallbackText ?? ""} disabled={!editable} onCommit={(fallbackText) => onPatch({ fallbackText: fallbackText || undefined })} /></label></>;
    }
    case "Image": {
      return <ImageSourceEditor element={element} reportType={reportType} resources={resources} editable={editable} client={client} onPatch={onPatch} onUploaded={onImageResourceUploaded} />;
    }
    case "PageNumber":
      return <><SelectField label="页码格式" value={element.format} options={[{ value: "CurrentOfTotal", label: "当前页 / 总页数" }, { value: "Current", label: "当前页" }]} disabled={!editable} onChange={(format) => onPatch({ format: format === "Current" ? "Current" : "CurrentOfTotal" })} /><label><span>前缀</span><CommitTextField value={element.prefix ?? ""} disabled={!editable} placeholder="例如：第 " onCommit={(prefix) => onPatch({ prefix: prefix || undefined })} /></label><label><span>后缀</span><CommitTextField value={element.suffix ?? ""} disabled={!editable} placeholder="例如： 页" onCommit={(suffix) => onPatch({ suffix: suffix || undefined })} /></label></>;
    case "Line":
      return <SelectField label="方向" value={element.direction} options={[{ value: "Horizontal", label: "水平" }, { value: "Vertical", label: "垂直" }]} disabled={!editable} onChange={(direction) => onPatch({ direction: direction === "Vertical" ? "Vertical" : "Horizontal" })} />;
    case "Flow":
      return <fieldset className="report-designer-v3-flow-editor" aria-label="表格与内容属性" disabled={!editable}><ReportDesignerV3FlowProperties block={element.block} fieldGroups={fieldGroups} selectedGridCellId={selectedGridCellId} onSelectGridCell={onSelectGridCell} onCommit={onFlowCommit} /></fieldset>;
    case "Rectangle":
      return null;
  }
}

function ElementStyleEditor({ element, editable, onPatch }: { element: ReportDesignerV3Element; editable: boolean; onPatch: (update: Partial<ReportDesignerV3ElementStyle>) => void }) {
  const style = element.style;
  const line = element.type === "Line";
  const text = element.type === "Text" || element.type === "Field" || element.type === "PageNumber";
  return <div className="report-designer-v3-style-editor">
    {text && <>
      <strong>文字</strong><div className="report-designer-v3-inspector-grid">
        <NumberField label="字号 pt" value={style.fontSizePt ?? 10} min={6} max={96} disabled={!editable} onCommit={(fontSizePt) => onPatch({ fontSizePt })} />
        <SelectField label="对齐" value={style.align ?? "Left"} options={[{ value: "Left", label: "左" }, { value: "Center", label: "中" }, { value: "Right", label: "右" }]} disabled={!editable} onChange={(align) => onPatch({ align: align as "Left" | "Center" | "Right" })} />
      </div>
      <CheckRow checked={style.bold === true} disabled={!editable} onChange={(bold) => onPatch({ bold })}>粗体</CheckRow>
      <ReportDesignerV3ColorField label="文字颜色" value={style.color ?? "#1f2933"} disabled={!editable} onCommit={(color) => onPatch({ color })} />
    </>}
    {!line && <ReportDesignerV3ColorField label="背景颜色" value={style.backgroundColor ?? ""} allowEmpty disabled={!editable} onCommit={(backgroundColor) => onPatch({ backgroundColor: backgroundColor || undefined })} />}
    <strong>{line ? "线条" : "边框"}</strong>
    <div className="report-designer-v3-inspector-grid">
      <SelectField label={line ? "线型" : "边框样式"} value={style.borderStyle ?? (style.borderWidthPx ? "Solid" : "None")} options={[{ value: "None", label: "无" }, { value: "Solid", label: "实线" }, { value: "Dashed", label: "虚线" }]} disabled={!editable}
        onChange={(value) => onPatch({ borderStyle: value as "None" | "Solid" | "Dashed", borderWidthPx: value === "None" ? style.borderWidthPx : Math.max(1, style.borderWidthPx ?? 1) })} />
      <NumberField label="线宽 px" value={style.borderWidthPx ?? (line ? 1 : 0)} min={line ? 1 : 0} max={8} disabled={!editable} onCommit={(borderWidthPx) => onPatch({ borderWidthPx })} />
    </div>
    <ReportDesignerV3ColorField label={line ? "线条颜色" : "边框颜色"} value={style.borderColor ?? "#334155"} disabled={!editable} onCommit={(borderColor) => onPatch({ borderColor })} />
    {(text || element.type === "Image") && <NumberField label="内边距 (mm)" value={hundredthMmToMm(style.paddingHundredthMm ?? 0)} min={0} max={20} disabled={!editable} onCommit={(value) => onPatch({ paddingHundredthMm: Math.round(value * 100) })} />}
  </div>;
}

function RegionSelector({ state, onCommit, disabled }: { state: ReportDesignerV3DocumentState; onCommit: (next: ReportDesignerV3DocumentState) => void; disabled: boolean }) {
  const located = state.selectedIds.map((id) => findV3Element(state.schema, id)).filter((item): item is LocatedElement => item !== null);
  const ids = new Set(located.map((item) => item.layer.id));
  const bodyOnly = located.some((item) => requiresV3BodyRegion(item.element));
  return <div className="report-designer-region-control"><label><span>所在区域</span><select aria-label="元素所在区域" disabled={disabled} value={ids.size === 1 ? located[0].layer.id : ""} onChange={(event) => onCommit(moveV3SelectionToRegion(state, event.target.value))}>
    {ids.size !== 1 ? <option value="" disabled>多个区域</option> : null}
    {state.schema.layers.map((layer) => <option key={layer.id} value={layer.id} disabled={layer.locked || !layer.visible || (bodyOnly && layer.role !== "Body")}>{layer.name}{layer.locked ? "（已锁定）" : !layer.visible ? "（已隐藏）" : ""}</option>)}
  </select></label><small>{bodyOnly ? "自动重复明细和分页符放在主体区域。" : "跨区域拖动时自动更新；覆盖层用于水印等固定内容。"}</small></div>;
}
