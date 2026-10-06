import { useEffect, useRef, type ReactNode } from "react";
import { Columns3, FilePlus2, Hash, Image as ImageIcon, ListFilter, Pilcrow, Table2 } from "lucide-react";
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
  productFields?: () => void;
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
      {actions.productFields ? <PaletteSection title="商品明细">
        <PaletteAction label="商品字段（逐行输出）" onClick={actions.productFields} icon={<Columns3 size={15} aria-hidden="true" />} disabled={!canEdit} />
      </PaletteSection> : null}
      <details><summary>高级排版</summary><PaletteSection title="表格与分组">
        <PaletteAction label="多列行" onClick={actions.row} icon={<Columns3 size={15} aria-hidden="true" />} disabled={!canEdit} />
        <PaletteAction label="普通表格" onClick={actions.grid} icon={<Table2 size={15} aria-hidden="true" />} disabled={!canEdit} />
        <PaletteAction label="条件块" onClick={actions.conditional} icon={<ListFilter size={15} aria-hidden="true" />} disabled={!canEdit} />
      </PaletteSection>
      {actions.detailTable ? <PaletteAction label="明细表（分组与组合排版）" onClick={actions.detailTable} icon={<Table2 size={15} aria-hidden="true" />} disabled={!canEdit} /> : null}
      <PaletteSection title="打印">
        <PaletteAction label="分页符" onClick={actions.pageBreak} icon={<FilePlus2 size={15} aria-hidden="true" />} disabled={!canEdit} />
      </PaletteSection>
      </details>
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
  reportType,
  query,
  groups,
  productFields = false,
  onProductFieldsChange,
  onQueryChange,
  onInsert,
  focusRequest = 0,
  canEdit = true,
}: {
  reportType: ReportDesignerReportType;
  query: string;
  groups: ReportDesignerFieldGroup[];
  productFields?: boolean;
  onProductFieldsChange?: (value: boolean) => void;
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
      {onProductFieldsChange ? <div className="report-designer-field-scope" role="tablist" aria-label="字段用途">
        {[false, true].map(value => <button key={String(value)} className={productFields === value ? "is-active" : ""} type="button" role="tab" aria-selected={productFields === value} onClick={() => onProductFieldsChange(value)}>{value ? "商品明细字段" : "普通字段"}</button>)}
      </div> : null}
      <div className="report-designer-v3-panel-caption">
        <Pilcrow size={15} aria-hidden="true" />
        <span>{productFields ? "商品明细字段" : "普通字段"}</span>
        <small role="status">{fieldCount} 个{query.trim() ? "匹配" : "可用"}字段</small>
      </div>
      <label className="report-designer-v3-field-search">
        <span>搜索字段</span>
        <input ref={searchRef} aria-label="搜索字段" value={query} placeholder={reportType === "PaymentVoucher" ? "付款单号、收款方、金额..." : "发票号、客户、金额..."} onChange={(event) => onQueryChange(event.target.value)} />
      </label>
      <p className="report-designer-v3-help">{reportType === "PaymentVoucher" ? "把付款单号、收款方、费用和金额等字段拖到纸上，调整位置后保存即可打印。" : productFields ? "把商品字段拖到明细行，各字段可单独移动。只设计一行，打印时按商品逐件重复。" : "把字段拖到纸上即可。发票号、唛头和合计等普通字段不随商品重复。"}</p>
      {groups.length === 0 ? <div className="report-designer-v3-help">
        <p>{query.trim() ? "没有找到匹配字段，请换个名称或清空搜索。" : "暂无可用字段"}</p>
        {query.trim() ? <button type="button" onClick={() => { onQueryChange(""); searchRef.current?.focus(); }}>清空搜索</button> : null}
      </div> : groups.map((group) => (
        <details key={group.category} open={Boolean(query.trim()) || groups.length <= 4}>
          <summary>{group.category}<small>{group.fields.length}</small></summary>
          <div className="report-designer-v3-field-list">
            {group.fields.map((field) => (
               <button type="button" key={field.value} disabled={!canEdit} draggable={canEdit} onDragStart={event => { event.dataTransfer.effectAllowed = "copy"; event.dataTransfer.setData("application/x-exportdoc-field", field.value); }} title={`拖动或点击添加 ${field.label}`} aria-label={`插入字段 ${field.label}`} onClick={() => onInsert(field)}>
                <span>{field.label}</span>
                <small>{field.value.startsWith("item.") ? "每件商品一行" : "单据信息"}</small>
              </button>
            ))}
          </div>
        </details>
      ))}
    </div>
  );
}
