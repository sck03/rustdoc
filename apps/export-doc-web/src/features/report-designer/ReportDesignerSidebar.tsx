import { type DragEvent, type ReactNode, useMemo, useState } from "react";
import { Columns3, FilePlus2, FileText, Grid2X2, Image as ImageIcon, ListFilter, Pilcrow, Table2 } from "lucide-react";
import {
  readReportDesignerDragPayload,
  type ReportDesignerDragPayload,
  type ReportDesignerPaletteComponentType,
  writeReportDesignerDragPayload,
} from "./reportDesignerDragDrop.ts";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  collectReportDesignerBlockFieldBindings,
  reportBlockModelRole,
  reportSectionModelRole,
  summarizeReportDesignerSchemaModel,
} from "./reportDesignerModel.ts";
import type { ReportDesignerBlockDropTarget } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDesignerSchema } from "./reportDesignerSchema.ts";
import { blockLabel, sectionLabel } from "./reportDesignerSelection.ts";

type ReportDesignerSidebarPanel = "components" | "model" | "fields";

export function ReportDesignerSidebar({
  schema,
  selectedBlockId,
  selectedSectionId,
  fieldGroups,
  canInsertDetailTable,
  onSelectBlock,
  onSelectSection,
  onInsertText,
  onInsertRow,
  onInsertGrid,
  onInsertConditional,
  onInsertImage,
  onInsertDetailTable,
  onInsertPageBreak,
  onInsertField,
  onDropDesignerItem,
}: {
  schema: ReportDesignerSchema;
  selectedBlockId: string | null;
  selectedSectionId: string | null;
  fieldGroups: ReportDesignerFieldGroup[];
  canInsertDetailTable: boolean;
  onSelectBlock: (blockId: string) => void;
  onSelectSection: (sectionId: string) => void;
  onInsertText: () => void;
  onInsertRow: () => void;
  onInsertGrid: () => void;
  onInsertConditional: () => void;
  onInsertImage: () => void;
  onInsertDetailTable: () => void;
  onInsertPageBreak: () => void;
  onInsertField: (field: { label: string; value: string }) => void;
  onDropDesignerItem: (payload: ReportDesignerDragPayload, target: ReportDesignerBlockDropTarget) => void;
}) {
  const [fieldQuery, setFieldQuery] = useState("");
  const [activePanel, setActivePanel] = useState<ReportDesignerSidebarPanel>("components");
  const visibleFieldGroups = useMemo(
    () => filterFieldGroups(fieldGroups, fieldQuery),
    [fieldGroups, fieldQuery],
  );
  const modelSummary = useMemo(() => summarizeReportDesignerSchemaModel(schema), [schema]);
  const blockCount = schema.sections.reduce((count, section) => count + section.blocks.length, 0);
  const fieldCount = fieldGroups.reduce((count, group) => count + group.fields.length, 0);

  return (
    <aside className="new-report-designer-sidebar">
      <div className="new-report-designer-sidebar-tabs" role="tablist" aria-label="设计器左侧面板">
        <button
          className={activePanel === "components" ? "segmented-active" : ""}
          type="button"
          role="tab"
          aria-selected={activePanel === "components"}
          onClick={() => setActivePanel("components")}
        >
          组件库
        </button>
        <button
          className={activePanel === "model" ? "segmented-active" : ""}
          type="button"
          role="tab"
          aria-selected={activePanel === "model"}
          onClick={() => setActivePanel("model")}
        >
          报表模型
        </button>
        <button
          className={activePanel === "fields" ? "segmented-active" : ""}
          type="button"
          role="tab"
          aria-selected={activePanel === "fields"}
          onClick={() => setActivePanel("fields")}
        >
          字段目录
        </button>
      </div>
      <section className="new-report-designer-panel new-report-designer-sidebar-panel">
        <div className="new-report-designer-panel-title">
          <FileText size={16} aria-hidden="true" />
          <span>{activePanel === "components" ? "组件库" : activePanel === "model" ? "报表模型" : "字段目录"}</span>
          <small>{activePanel === "components" ? (canInsertDetailTable ? 8 : 7) : activePanel === "model" ? blockCount : fieldCount}</small>
        </div>
        {activePanel === "components" ? (
          <div className="new-report-component-palette">
            <div className="new-report-component-group">
              <span className="new-report-component-group-title">基础</span>
              <PaletteButton componentType="Text" icon={<Pilcrow size={15} aria-hidden="true" />} label="文本" onClick={onInsertText} />
              <PaletteButton componentType="Row" icon={<Columns3 size={15} aria-hidden="true" />} label="多列行" onClick={onInsertRow} />
              <PaletteButton componentType="Grid" icon={<Grid2X2 size={15} aria-hidden="true" />} label="票据格" onClick={onInsertGrid} />
            </div>
            <div className="new-report-component-group">
              <span className="new-report-component-group-title">业务</span>
              <PaletteButton componentType="Conditional" icon={<ListFilter size={15} aria-hidden="true" />} label="条件块" onClick={onInsertConditional} />
              <PaletteButton componentType="Image" icon={<ImageIcon size={15} aria-hidden="true" />} label="图片/印章" onClick={onInsertImage} />
              {canInsertDetailTable ? (
                <PaletteButton componentType="DetailTable" icon={<Table2 size={15} aria-hidden="true" />} label="明细表" onClick={onInsertDetailTable} />
              ) : null}
            </div>
            <div className="new-report-component-group">
              <span className="new-report-component-group-title">打印</span>
              <PaletteButton componentType="PageBreak" icon={<FilePlus2 size={15} aria-hidden="true" />} label="分页符" onClick={onInsertPageBreak} />
            </div>
          </div>
        ) : null}
        {activePanel === "model" ? (
          <div className="new-report-model-tree">
            <div className="new-report-model-overview" aria-label="报表模型概览">
              <div>
                <span>类型</span>
                <strong>{modelSummary.reportTypeLabel}</strong>
              </div>
              <div>
                <span>纸张</span>
                <strong>{modelSummary.pageLabel}</strong>
              </div>
              <div>
                <span>数据源</span>
                <strong>{modelSummary.dataSources.join(" / ") || "-"}</strong>
              </div>
              <div>
                <span>字段绑定</span>
                <strong>{modelSummary.fieldBindingCount}</strong>
              </div>
              {modelSummary.disabledBlockCount > 0 ? (
                <div>
                  <span>停用组件</span>
                  <strong>{modelSummary.disabledBlockCount}</strong>
                </div>
              ) : null}
            </div>
            {schema.sections.map((section) => (
              <details
                key={section.id}
                className={selectedSectionId === section.id ? "new-report-model-section-selected" : ""}
                open
                onDragOver={handleDesignerDragOver}
                onDrop={(event) => handleDrop(event, { sectionId: section.id, placement: "inside" }, onDropDesignerItem)}
              >
                <summary onClick={() => onSelectSection(section.id)}>
                  <span className="new-report-model-section-title">
                    <strong>{sectionLabel(section)}</strong>
                    <small>{reportSectionModelRole(section)} / {renderSectionPrintHint(section)}</small>
                  </span>
                  <strong className="new-report-model-section-count">{section.blocks.length}</strong>
                </summary>
                {section.blocks.length === 0 ? (
                  <div className="new-report-model-empty">空版区</div>
                ) : (
                  <div className="new-report-model-block-list">
                    {section.blocks.map((block, index) => (
                      <div className="new-report-model-block-node" key={block.id}>
                        <button
                          className={[
                            "new-report-model-block",
                            selectedBlockId === block.id ? "new-report-model-block-selected" : "",
                            block.output?.enabled === false ? "new-report-model-block-output-disabled" : "",
                          ].filter(Boolean).join(" ")}
                          type="button"
                          draggable
                          title={`${sectionLabel(section)} / ${blockLabel(block)}`}
                          onDragStart={(event) => writeReportDesignerDragPayload(event, { kind: "Block", blockId: block.id })}
                          onDragOver={handleDesignerDragOver}
                          onDrop={(event) => handleDrop(event, resolveBlockDropTarget(event, section.id, block.id), onDropDesignerItem)}
                          onClick={(event) => {
                            event.stopPropagation();
                            onSelectBlock(block.id);
                          }}
                        >
                          <strong>
                            {index + 1}. {blockLabel(block)}
                          </strong>
                          <small>{renderBlockMeta(block)}</small>
                        </button>
                        {renderBlockModelChildren(block)}
                      </div>
                    ))}
                  </div>
                )}
              </details>
            ))}
          </div>
        ) : null}
        {activePanel === "fields" ? (
          <>
            <label className="new-report-field-search">
              <span>查找字段</span>
              <input
                value={fieldQuery}
                placeholder="发票号、客户、金额..."
                onChange={(event) => setFieldQuery(event.target.value)}
              />
            </label>
            {fieldGroups.length === 0 ? (
              <div className="new-report-designer-muted">字段目录加载中或暂无字段</div>
            ) : visibleFieldGroups.length === 0 ? (
              <div className="new-report-designer-muted">没有匹配的字段</div>
            ) : (
              <div className="new-report-field-groups">
                {visibleFieldGroups.map((group) => (
                  <details key={group.category} open={fieldQuery.trim().length > 0 || visibleFieldGroups.length <= 4}>
                    <summary>{group.category}</summary>
                    <div className="new-report-field-list">
                      {group.fields.map((field) => (
                        <button
                          key={field.value}
                          type="button"
                          draggable
                          title={field.value}
                          onDragStart={(event) =>
                            writeReportDesignerDragPayload(event, {
                              kind: "Field",
                              label: field.label,
                              value: field.value,
                            })
                          }
                          onClick={() => onInsertField(field)}
                        >
                          <span>{field.label}</span>
                          <small>{field.value}</small>
                        </button>
                      ))}
                    </div>
                  </details>
                ))}
              </div>
            )}
          </>
        ) : null}
      </section>
    </aside>
  );
}

function PaletteButton({
  componentType,
  icon,
  label,
  onClick,
}: {
  componentType: ReportDesignerPaletteComponentType;
  icon: ReactNode;
  label: string;
  onClick: () => void;
}) {
  return (
    <button
      className="new-report-component-button"
      type="button"
      draggable
      onDragStart={(event) => writeReportDesignerDragPayload(event, { kind: "Component", componentType })}
      onClick={onClick}
    >
      {icon}
      <span>{label}</span>
    </button>
  );
}

function handleDesignerDragOver(event: DragEvent<HTMLElement>) {
  event.preventDefault();
  event.dataTransfer.dropEffect = "copy";
}

function resolveBlockDropTarget(
  event: DragEvent<HTMLElement>,
  sectionId: string,
  blockId: string,
): ReportDesignerBlockDropTarget {
  const bounds = event.currentTarget.getBoundingClientRect();
  const placement = event.clientY < bounds.top + bounds.height / 2 ? "before" : "after";
  return {
    sectionId,
    blockId,
    placement,
  };
}

function handleDrop(
  event: DragEvent<HTMLElement>,
  target: ReportDesignerBlockDropTarget,
  onDropDesignerItem?: (payload: ReportDesignerDragPayload, target: ReportDesignerBlockDropTarget) => void,
) {
  const payload = readReportDesignerDragPayload(event);
  if (!payload || !onDropDesignerItem) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  onDropDesignerItem(payload, target);
}

function renderSectionPrintHint(section: ReportDesignerSchema["sections"][number]) {
  const hints = [
    section.print.minHeightMm ? `高 ${formatMm(section.print.minHeightMm)}` : "",
    section.print.repeatOnEveryPage ? "跨页重复" : "",
    section.print.keepTogether ? "避免拆分" : "",
    section.print.pinToPageBottom ? "页底" : "",
  ].filter(Boolean);

  return hints.length > 0 ? hints.join(" / ") : "普通";
}

function formatMm(value: number) {
  return `${Math.round(value * 10) / 10}mm`;
}

function renderBlockMeta(block: ReportBlock) {
  const role = reportBlockModelRole(block);
  const outputMeta = renderBlockOutputMeta(block);
  switch (block.type) {
    case "Text":
      return normalizeBlockMeta(`${role} / ${block.text || "固定文本"}${outputMeta}`);
    case "Field":
      return normalizeBlockMeta(`${role} / ${block.fieldPath || "字段"}${outputMeta}`);
    case "Row":
      return normalizeBlockMeta(`${role} / ${block.columns.length} 列${outputMeta}`);
    case "Grid":
      return normalizeBlockMeta(`${role} / ${block.rows.length} 行 / ${block.columns.length} 列${outputMeta}`);
    case "Conditional":
      return normalizeBlockMeta(`${role} / ${block.condition.fieldPath} ${conditionOperatorLabel(block.condition.operator)}${outputMeta}`);
    case "Image":
      return normalizeBlockMeta(`${role} / ${block.sourceKind === "Field" ? block.fieldPath : block.url || "静态地址"}${outputMeta}`);
    case "DetailTable":
      return normalizeBlockMeta([
        role,
        block.sourcePath,
        `${block.columns.length} 列`,
        block.grouping ? "分组" : "",
        block.grouping?.pageBreakBefore ? "另起页" : "",
        block.grouping?.footer ? "小计" : "",
      ].filter(Boolean).join(" / ") + outputMeta);
    case "PageBreak":
      return normalizeBlockMeta(`${role} / 强制换页${outputMeta}`);
  }
}

function renderBlockOutputMeta(block: ReportBlock) {
  const disabled = block.output?.enabled === false ? " / 不输出" : "";
  const note = block.output?.note ? ` / 备注: ${block.output.note}` : "";
  return `${disabled}${note}`;
}

function renderBlockModelChildren(block: ReportBlock) {
  const bindings = collectReportDesignerBlockFieldBindings(block);
  const nodes = [
    ...renderBlockStructuralNodes(block),
    ...bindings.map((binding) => `${binding.label}: ${binding.fieldPath}`),
  ];

  if (nodes.length === 0) {
    return null;
  }

  return (
    <div className="new-report-model-subnodes" aria-label="明细表报表模型">
      {nodes.map((node) => (
        <span key={node}>{node}</span>
      ))}
    </div>
  );
}

function renderBlockStructuralNodes(block: ReportBlock) {
  if (block.type !== "DetailTable") {
    return [];
  }

  return [
    block.print.repeatHeaderOnPageBreak ? "跨页表头" : "表头不重复",
    block.print.keepRowsTogether ? "明细行避免拆分" : "明细行可拆分",
    block.sideBand ? `非循环侧栏: ${block.sideBand.title}` : "",
    block.grouping ? `分组表头: ${block.grouping.label}` : "",
    block.grouping?.pageBreakBefore ? "分组另起页" : "",
    block.grouping?.footer ? `分组小计: ${block.grouping.footer.label}` : "",
    block.summaryRow ? `表尾合计: ${block.summaryRow.label}` : "",
  ].filter(Boolean);
}

function conditionOperatorLabel(operator: Extract<ReportBlock, { type: "Conditional" }>["condition"]["operator"]) {
  switch (operator) {
    case "HasValue":
      return "有值";
    case "Equals":
      return "等于";
    case "NotEquals":
      return "不等于";
  }
}

function normalizeBlockMeta(value: string) {
  const normalized = value.replace(/\s+/g, " ").trim();
  return normalized || "-";
}

function filterFieldGroups(fieldGroups: ReportDesignerFieldGroup[], query: string) {
  const normalizedQuery = query.trim().toLowerCase();
  if (!normalizedQuery) {
    return fieldGroups;
  }

  return fieldGroups
    .map((group) => ({
      ...group,
      fields: group.fields.filter((field) =>
        group.category.toLowerCase().includes(normalizedQuery) ||
        field.label.toLowerCase().includes(normalizedQuery) ||
        field.value.toLowerCase().includes(normalizedQuery) ||
        (field.originalValue ?? "").toLowerCase().includes(normalizedQuery),
      ),
    }))
    .filter((group) => group.fields.length > 0);
}
