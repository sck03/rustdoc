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

export function DetailTableProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const [tab, setTab] = useState<"columns" | "layout" | "groups">("columns");
  const fixedFields = useMemo(() => documentReportFieldGroups(fieldGroups), [fieldGroups]);
  return (
    <div className="new-report-detail-properties">
      <p className="new-report-designer-muted">每件商品自动显示一行。先选择要修改的列，再调整标题、内容和宽度。</p>
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "columns", label: "商品列" }, { value: "layout", label: "表格样式" }, { value: "groups", label: "分页与高级" }]}>
        <VisitedPropertyPanel active={tab === "columns"}><ReportDesignerDetailTableColumnsProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></VisitedPropertyPanel>
        <VisitedPropertyPanel active={tab === "layout"}><ReportDesignerDetailTableLayoutProperties block={block} onCommit={onCommit} /></VisitedPropertyPanel>
        <VisitedPropertyPanel active={tab === "groups"}>
          <ReportDesignerDetailTablePrintProperties block={block} fieldGroups={fixedFields} onCommit={onCommit} />
          <DeferredPropertySection title="分组与小计"><ReportDesignerDetailTableGroupingProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></DeferredPropertySection>
          <DeferredPropertySection title="末页合计"><ReportDesignerDetailTableSummaryProperties block={block} fieldGroups={fixedFields} onCommit={onCommit} /></DeferredPropertySection>
          <DeferredPropertySection title="定金与费用调整"><ReportDesignerDetailAdjustmentsProperties block={block} onCommit={onCommit} /></DeferredPropertySection>
          <DeferredPropertySection title="首页说明行"><ReportDesignerDetailTableSummaryProperties title="明细前说明行（仅首页）" block={{...block, summaryRow:block.introRow}} fieldGroups={fixedFields} onCommit={next => { if(next.type === "DetailTable") onCommit({...block,introRow:next.summaryRow}); }} /></DeferredPropertySection>
        </VisitedPropertyPanel>
      </DesignerPropertyTabs>
    </div>
  );
}
