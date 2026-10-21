import { useMemo, useState } from "react";
import { documentReportFieldGroups, type ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { ReportDesignerDetailTableColumnsProperties } from "./ReportDesignerDetailTableColumnsProperties.tsx";
import { ReportDesignerDetailTableGroupingProperties } from "./ReportDesignerDetailTableGroupingProperties.tsx";
import { ReportDesignerDetailTableLayoutProperties } from "./ReportDesignerDetailTableLayoutProperties.tsx";
import { ReportDesignerDetailTableSummaryProperties } from "./ReportDesignerDetailTableSummaryProperties.tsx";
import { ReportDesignerDetailTablePrintProperties } from "./ReportDesignerDetailTablePrintProperties.tsx";
import { DesignerPropertyTabs } from "./ReportDesignerPropertyControls.tsx";
import { ReportDesignerDetailAdjustmentsProperties } from "./ReportDesignerDetailAdjustmentsProperties.tsx";
import { DeferredPropertySection, VisitedPropertyPanel } from "./ReportDesignerDeferredProperties.tsx";
import { useReportDesignerPreview } from "./ReportDesignerPreviewContext.tsx";

export function DetailTableProperties({
  block,
  widthMm,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  widthMm: number;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const [tab, setTab] = useState<"columns" | "layout" | "entries" | "groups">("columns");
  const preview = useReportDesignerPreview();
  const fixedFields = useMemo(() => documentReportFieldGroups(fieldGroups), [fieldGroups]);
  return (
    <div className="new-report-detail-properties">
      <label><span>按录入行打印的说明列</span><select value={block.entryDescriptionColumnId ?? ""} onChange={event => onCommit({ ...block, entryDescriptionColumnId: event.target.value || undefined })}>
        <option value="">仅使用商品资料</option>{block.columns.map((column, index) => <option key={column.id} value={column.id}>{index + 1}. {column.title || "未命名列"}</option>)}
      </select></label>
      <label><span>画布示例</span><select value={block.entryDescriptionColumnId ? preview.mode : "products"} onChange={event => preview.setMode(event.target.value === "entries" ? "entries" : "products")}><option value="products">商品资料自动组合</option><option value="entries" disabled={!block.entryDescriptionColumnId}>按录入行排列</option></select></label>
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "columns", label: "明细列" }, { value: "layout", label: "表格样式" }, { value: "entries", label: "说明与合计" }, { value: "groups", label: "分页与高级" }]}>
        <VisitedPropertyPanel active={tab === "columns"}><ReportDesignerDetailTableColumnsProperties block={block} widthMm={widthMm} fieldGroups={fieldGroups} onCommit={onCommit} /></VisitedPropertyPanel>
        <VisitedPropertyPanel active={tab === "layout"}><ReportDesignerDetailTableLayoutProperties block={block} onCommit={onCommit} /></VisitedPropertyPanel>
        <VisitedPropertyPanel active={tab === "entries"}>
          <DeferredPropertySection title="末页合计"><ReportDesignerDetailTableSummaryProperties block={block} fieldGroups={fixedFields} onCommit={onCommit} /></DeferredPropertySection>
          <DeferredPropertySection title="金额调整"><ReportDesignerDetailAdjustmentsProperties block={block} onCommit={onCommit} /></DeferredPropertySection>
          <DeferredPropertySection title="首页说明行"><ReportDesignerDetailTableSummaryProperties title="明细前说明行（仅首页）" block={{...block, summaryRow:block.introRow}} fieldGroups={fixedFields} onCommit={next => { if(next.type === "DetailTable") onCommit({...block,introRow:next.summaryRow}); }} /></DeferredPropertySection>
        </VisitedPropertyPanel>
        <VisitedPropertyPanel active={tab === "groups"}>
          <ReportDesignerDetailTablePrintProperties block={block} fieldGroups={fixedFields} onCommit={onCommit} />
          <DeferredPropertySection title="商品资料分组与小计"><ReportDesignerDetailTableGroupingProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></DeferredPropertySection>
        </VisitedPropertyPanel>
      </DesignerPropertyTabs>
    </div>
  );
}
