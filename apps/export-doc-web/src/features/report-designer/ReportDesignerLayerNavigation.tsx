import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { reportDesignerV3ElementKindLabel, reportDesignerV3ElementText } from "./reportDesignerV3Schema.ts";
import { v3RegionNames } from "./reportDesignerV3Regions.ts";

/** View state only: navigation must never alter a template's visibility or print rules. */
export function ReportDesignerLayerNavigation({ state, onSelect }: {
  state: ReportDesignerV3DocumentState;
  onSelect: (id: string) => void;
}) {
  const selected = state.selectedIds.length === 1
    ? state.schema.layers.flatMap(layer => layer.elements).find(element => element.id === state.selectedIds[0]) : null;
  return <div className="report-designer-layer-navigation">
    <div role="group" aria-label="画布图层导航" className="report-designer-layer-tabs">
      {state.schema.layers.map(layer => <button key={layer.id} type="button" data-layer-role={layer.role}
        aria-pressed={state.activeLayerId === layer.id} onClick={() => onSelect(layer.id)}
        title={`${layer.name} · ${v3RegionNames[layer.role]}${layer.visible ? "" : " · 已隐藏"}${layer.locked ? " · 已锁定" : ""}`}>
        <strong>{layer.name}</strong><small>{v3RegionNames[layer.role]} · {layer.elements.length} 项{layer.visible ? "" : " · 隐藏"}{layer.locked ? " · 锁定" : ""}</small>
      </button>)}
    </div>
    <div className="report-designer-selection-readout" role="status">
      {selected ? <><strong>{reportDesignerV3ElementKindLabel(selected)}</strong><span>{reportDesignerV3ElementText(selected)}</span></>
        : state.selectedIds.length ? `已选 ${state.selectedIds.length} 个组件`
          : "选择图层定位设计区；点击组件编辑，双击文字修改内容。"}
    </div>
  </div>;
}
