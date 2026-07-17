import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { ReportDesignerDetailTableColumnsProperties } from "./ReportDesignerDetailTableColumnsProperties.tsx";
import { ReportDesignerDetailTableGroupingProperties } from "./ReportDesignerDetailTableGroupingProperties.tsx";
import { ReportDesignerDetailTableLayoutProperties } from "./ReportDesignerDetailTableLayoutProperties.tsx";
import { ReportDesignerDetailTableSummaryProperties } from "./ReportDesignerDetailTableSummaryProperties.tsx";

export function DetailTableProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  return (
    <div className="new-report-detail-properties">
      <ReportDesignerDetailTableLayoutProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} />
      <ReportDesignerDetailTableGroupingProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} />
      <ReportDesignerDetailTableColumnsProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} />
      <ReportDesignerDetailTableSummaryProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} />
    </div>
  );
}
