import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { ReportDesignerDetailTableColumnsProperties } from "./ReportDesignerDetailTableColumnsProperties.tsx";
import { ReportDesignerDetailTableGroupingProperties } from "./ReportDesignerDetailTableGroupingProperties.tsx";
import { ReportDesignerDetailTableLayoutProperties } from "./ReportDesignerDetailTableLayoutProperties.tsx";
import { ReportDesignerDetailTableSummaryProperties } from "./ReportDesignerDetailTableSummaryProperties.tsx";
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
      <DesignerPropertyTabs value={tab} onChange={setTab} options={[{ value: "columns", label: "明细列" }, { value: "layout", label: "版式" }, { value: "groups", label: "分组汇总" }]}>
        {tab === "columns" ? <ReportDesignerDetailTableColumnsProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
        {tab === "layout" ? <ReportDesignerDetailTableLayoutProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
        {tab === "groups" ? <><ReportDesignerDetailTableGroupingProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /><ReportDesignerDetailTableSummaryProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /></> : null}
      </DesignerPropertyTabs>
    </div>
  );
}
import { useState } from "react";
