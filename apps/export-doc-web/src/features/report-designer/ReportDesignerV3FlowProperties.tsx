import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { ConditionalBlockProperties } from "./ReportDesignerConditionalProperties.tsx";
import { DetailTableProperties } from "./ReportDesignerDetailTableProperties.tsx";
import { GridBlockProperties } from "./ReportDesignerGridProperties.tsx";
import { RowBlockProperties } from "./ReportDesignerRowProperties.tsx";
import type { ReportBlock } from "./reportDesignerSchema.ts";

type EditableFlowBlock = Extract<ReportBlock, {
  type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak";
}>;

/**
 * V3 owns position and size, while these small editors continue to own the
 * validated business AST.  Keeping this adapter thin prevents a second set of
 * row/grid/detail-table rules from growing inside the canvas inspector.
 */
export function ReportDesignerV3FlowProperties({
  block,
  fieldGroups,
  selectedGridCellId,
  onSelectGridCell,
  onCommit,
}: {
  block: EditableFlowBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  selectedGridCellId?: string;
  onSelectGridCell: (cellId: string) => void;
  onCommit: (block: EditableFlowBlock) => void;
}) {
  switch (block.type) {
    case "Row":
      return <RowBlockProperties block={block} fieldGroups={fieldGroups} onCommit={asFlowCommit(onCommit, "Row")} />;
    case "Grid":
      return <GridBlockProperties block={block} fieldGroups={fieldGroups} selectedCellId={selectedGridCellId} onSelectCell={onSelectGridCell} onCommit={asFlowCommit(onCommit, "Grid")} />;
    case "Conditional":
      return <ConditionalBlockProperties block={block} fieldGroups={fieldGroups} onCommit={asFlowCommit(onCommit, "Conditional")} />;
    case "DetailTable":
      return <DetailTableProperties block={block} fieldGroups={fieldGroups} onCommit={asFlowCommit(onCommit, "DetailTable")} />;
    case "PageBreak":
      return <div className="report-designer-v3-flow-properties-note">分页符会在打印时开始新页；可在画布中移动其位置。</div>;
  }
}

function asFlowCommit(
  onCommit: (block: EditableFlowBlock) => void,
  type: EditableFlowBlock["type"],
) {
  return (next: ReportBlock) => {
    if (next.type === type) onCommit(next as EditableFlowBlock);
  };
}
