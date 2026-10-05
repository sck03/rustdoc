import { useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { ReportDesignerDetailTableColumnsProperties } from "./ReportDesignerDetailTableColumnsProperties.tsx";
import { ReportDesignerDetailTableGroupingProperties } from "./ReportDesignerDetailTableGroupingProperties.tsx";
import { ReportDesignerDetailTableLayoutProperties } from "./ReportDesignerDetailTableLayoutProperties.tsx";
import { ReportDesignerDetailTableSummaryProperties } from "./ReportDesignerDetailTableSummaryProperties.tsx";
import { ReportDesignerDetailTablePrintProperties } from "./ReportDesignerDetailTablePrintProperties.tsx";
import { DesignerPropertyTabs } from "./ReportDesignerPropertyControls.tsx";

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
  return (
    <div className="new-report-detail-properties">
      <p className="new-report-designer-muted">每件商品自动显示一行。先选择要修改的列，再调整标题、内容和宽度。</p>
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "columns", label: "商品列" }, { value: "layout", label: "表格样式" }, { value: "groups", label: "分页与高级" }]}>
        <div hidden={tab !== "columns"}><ReportDesignerDetailTableColumnsProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></div>
        <div hidden={tab !== "layout"}><ReportDesignerDetailTableLayoutProperties block={block} onCommit={onCommit} /></div>
        <div hidden={tab !== "groups"}>
          <ReportDesignerDetailTablePrintProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} />
          <details><summary>分组与小计</summary><ReportDesignerDetailTableGroupingProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></details>
          <details><summary>末页合计</summary><ReportDesignerDetailTableSummaryProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></details>
          <details><summary>首页说明行</summary><ReportDesignerDetailTableSummaryProperties title="明细前说明行（仅首页）" block={{...block, summaryRow:block.introRow}} fieldGroups={fieldGroups} onCommit={next => { if(next.type === "DetailTable") onCommit({...block,introRow:next.summaryRow}); }} /></details>
        </div>
      </DesignerPropertyTabs>
    </div>
  );
}
