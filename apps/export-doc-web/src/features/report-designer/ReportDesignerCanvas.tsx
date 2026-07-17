import type { CSSProperties, DragEvent } from "react";
import {
  readReportDesignerDragPayload,
  type ReportDesignerDragPayload,
  writeReportDesignerDragPayload,
} from "./reportDesignerDragDrop.ts";
import type { ReportDesignerBlockDropTarget } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportBorderStyle, ReportDesignerSchema, ReportTextStyle } from "./reportDesignerSchema.ts";
import { blockLabel, sectionLabel } from "./reportDesignerSelection.ts";

export function ReportDesignerCanvas({
  schema,
  selectedBlockId,
  selectedSectionId,
  onSelectBlock,
  onSelectSection,
  onDropDesignerItem,
}: {
  schema: ReportDesignerSchema;
  selectedBlockId: string | null;
  selectedSectionId: string | null;
  onSelectBlock: (blockId: string) => void;
  onSelectSection: (sectionId: string) => void;
  onDropDesignerItem: (payload: ReportDesignerDragPayload, target: ReportDesignerBlockDropTarget) => void;
}) {
  const pageSize = readPageSize(schema);
  const marginStyle = {
    paddingTop: `${schema.page.marginTopMm}mm`,
    paddingRight: `${schema.page.marginRightMm}mm`,
    paddingBottom: `${schema.page.marginBottomMm}mm`,
    paddingLeft: `${schema.page.marginLeftMm}mm`,
  };

  return (
    <div className="new-report-designer-canvas" aria-label="新版报表设计画布">
      <div
        className="new-report-page"
        style={{
          width: `${pageSize.widthMm}mm`,
          minHeight: `${pageSize.heightMm}mm`,
          fontFamily: schema.page.fontFamily,
          fontSize: `${schema.page.fontSizePt}pt`,
        }}
      >
        <div className="new-report-page-margin" style={marginStyle}>
          {schema.sections.map((section) => (
            <section
              key={section.id}
              className={`new-report-section new-report-section-${section.type.toLowerCase()}${selectedSectionId === section.id ? " new-report-section-selected" : ""}`}
              style={toReactSectionStyle(section)}
              onDragOver={handleDesignerDragOver}
              onDrop={(event) => handleDrop(event, { sectionId: section.id, placement: "inside" }, onDropDesignerItem)}
              onClick={() => onSelectSection(section.id)}
            >
              <div className="new-report-section-label">
                <span>{sectionLabel(section)}</span>
                <span>{renderSectionPrintHint(section)}</span>
              </div>
              {section.blocks.length === 0 ? (
                <div className="new-report-empty-block">暂无组件</div>
              ) : (
                section.blocks.map((block) => (
                  <button
                    key={block.id}
                    className={[
                      "new-report-block",
                      selectedBlockId === block.id ? "new-report-block-selected" : "",
                      block.output?.enabled === false ? "new-report-block-output-disabled" : "",
                    ].filter(Boolean).join(" ")}
                    type="button"
                    draggable
                    onDragStart={(event) => writeReportDesignerDragPayload(event, { kind: "Block", blockId: block.id })}
                    onDragOver={handleDesignerDragOver}
                    onDrop={(event) => handleDrop(event, resolveBlockDropTarget(event, section.id, block.id), onDropDesignerItem)}
                    onClick={(event) => {
                      event.stopPropagation();
                      onSelectBlock(block.id);
                    }}
                  >
                    <span className="new-report-block-kind">
                      {blockLabel(block)}
                      {block.output?.enabled === false ? <span className="new-report-output-badge">不输出</span> : null}
                    </span>
                    {renderBlock(block)}
                    {block.output?.note ? <span className="new-report-output-note">{block.output.note}</span> : null}
                  </button>
                ))
              )}
            </section>
          ))}
        </div>
      </div>
    </div>
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
  onDropDesignerItem: (payload: ReportDesignerDragPayload, target: ReportDesignerBlockDropTarget) => void,
) {
  const payload = readReportDesignerDragPayload(event);
  if (!payload) {
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

function toReactSectionStyle(section: ReportDesignerSchema["sections"][number]): CSSProperties {
  return {
    minHeight: section.print.minHeightMm && section.print.minHeightMm > 0 ? `${section.print.minHeightMm}mm` : undefined,
  };
}

function formatMm(value: number) {
  return `${Math.round(value * 10) / 10}mm`;
}

function renderBlock(block: ReportBlock) {
  switch (block.type) {
    case "Text":
      return <span style={{ ...toReactTextStyle(block.style), ...toReactBorderStyle(block.border) }}>{block.text}</span>;
    case "Field":
      return (
        <span style={{ ...toReactTextStyle(block.style), ...toReactBorderStyle(block.border) }}>
          {block.label ? `${block.label}: ` : ""}
          <span className="new-report-field-token">{`{{ ${block.fieldPath} }}`}</span>
        </span>
      );
    case "Row":
      return renderRowBlock(block);
    case "Grid":
      return renderGridBlock(block);
    case "Conditional":
      return (
        <span className="new-report-conditional-preview" style={{ ...toReactTextStyle(block.style), ...toReactBorderStyle(block.border) }}>
          <span className="new-report-condition-token">{renderConditionLabel(block)}</span>
          {block.content.kind === "Field" ? (
            <span>
              {block.content.label ? `${block.content.label}: ` : ""}
              <span className="new-report-field-token">{`{{ ${block.content.fieldPath} }}`}</span>
            </span>
          ) : (
            <span>{block.content.text}</span>
          )}
        </span>
      );
    case "Image":
      return renderImageBlock(block);
    case "DetailTable":
      return renderDetailTable(block);
    case "PageBreak":
      return <span className="new-report-page-break">分页符</span>;
  }
}

function renderRowBlock(block: Extract<ReportBlock, { type: "Row" }>) {
  return (
    <table
      className="new-report-row-block"
      style={{
        marginTop: block.marginTopMm ? `${block.marginTopMm}mm` : undefined,
        marginBottom: block.marginBottomMm ? `${block.marginBottomMm}mm` : undefined,
      }}
    >
      <tbody>
        <tr>
          {block.columns.map((column) => (
            <td
              key={column.id}
              style={{
                width: `${column.widthPercent}%`,
                ...toReactTextStyle(column.style),
                display: undefined,
                ...toReactBorderStyle(column.border),
              }}
            >
              {column.contentKind === "Field" ? (
                <span>
                  {column.label ? `${column.label}: ` : ""}
                  <span className="new-report-field-token">{`{{ ${column.fieldPath} }}`}</span>
                </span>
              ) : (
                column.text
              )}
            </td>
          ))}
        </tr>
      </tbody>
    </table>
  );
}

function renderGridBlock(block: Extract<ReportBlock, { type: "Grid" }>) {
  return (
    <table
      className="new-report-grid-block"
      style={{
        marginTop: block.marginTopMm ? `${block.marginTopMm}mm` : undefined,
        marginBottom: block.marginBottomMm ? `${block.marginBottomMm}mm` : undefined,
      }}
    >
      <colgroup>
        {block.columns.map((column) => (
          <col key={column.id} style={{ width: `${Math.max(1, column.widthPercent)}%` }} />
        ))}
      </colgroup>
      <tbody>
        {block.rows.map((row) => (
          <tr key={row.id} style={{ height: row.heightMm ? `${row.heightMm}mm` : undefined }}>
            {row.cells.map((cell) => (
              <td
                key={cell.id}
                colSpan={Math.max(1, Math.floor(cell.colSpan ?? 1))}
                rowSpan={Math.max(1, Math.floor(cell.rowSpan ?? 1))}
                style={{
                  ...toReactTextStyle(cell.style),
                  display: undefined,
                  writingMode: cell.verticalText ? "vertical-rl" : undefined,
                  textAlign: cell.style.align ? alignToCss(cell.style.align) : undefined,
                  verticalAlign: "middle",
                  whiteSpace: "pre-wrap",
                  ...toReactBorderStyle(cell.border ?? block.border),
                }}
              >
                {renderGridCellPreview(cell)}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function renderGridCellPreview(cell: Extract<ReportBlock, { type: "Grid" }>["rows"][number]["cells"][number]) {
  switch (cell.contentKind) {
    case "Field":
      return (
        <span>
          {cell.label ? `${cell.label}: ` : ""}
          <span className="new-report-field-token">{`{{ ${cell.fieldPath} }}`}</span>
        </span>
      );
    case "CheckboxGroup":
      return (
        <span className="new-report-checkbox-preview">
          {(cell.checkboxOptions ?? []).map((option) => (
            <span key={option.id}>
              <span className="new-report-field-token">□</span>
              {option.label}
            </span>
          ))}
        </span>
      );
    case "Text":
      return cell.text;
  }
}

function renderImageBlock(block: Extract<ReportBlock, { type: "Image" }>) {
  const wrapperStyle: CSSProperties = {
    textAlign: alignToCss(block.align),
    marginTop: block.marginTopMm ? `${block.marginTopMm}mm` : undefined,
    marginBottom: block.marginBottomMm ? `${block.marginBottomMm}mm` : undefined,
  };
  const imageStyle: CSSProperties = {
    width: `${block.widthMm}mm`,
    height: block.heightMm ? `${block.heightMm}mm` : undefined,
    objectFit: "contain",
  };

  return (
    <span className="new-report-image-preview" style={wrapperStyle}>
      {block.sourceKind === "StaticUrl" && block.url ? (
        <img src={block.url} alt={block.altText || block.title || "Report image"} style={imageStyle} />
      ) : (
        <span className="new-report-image-placeholder" style={imageStyle}>
          <span>{block.title || "图片/印章"}</span>
          <span className="new-report-field-token">{block.sourceKind === "Field" ? `{{ ${block.fieldPath} }}` : "静态图片地址"}</span>
        </span>
      )}
      {block.hideWhenSourceEmpty ? <span className="new-report-image-rule">空源不打印</span> : null}
    </span>
  );
}

function renderConditionLabel(block: Extract<ReportBlock, { type: "Conditional" }>) {
  switch (block.condition.operator) {
    case "Equals":
      return `条件: ${block.condition.fieldPath} = ${block.condition.value}`;
    case "NotEquals":
      return `条件: ${block.condition.fieldPath} != ${block.condition.value}`;
    case "HasValue":
      return `条件: ${block.condition.fieldPath} 有值`;
  }
}

function renderDetailTable(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const table = (
    <table className="new-report-detail-table">
      <thead>
        {block.columns.some((column) => column.headerGroupTitle) ? (
          <tr className="new-report-detail-header-group-row">
            {renderDetailHeaderGroupCells(block)}
          </tr>
        ) : null}
        <tr>
          {block.columns.map((column) => (
            <th
              key={column.id}
              style={{
                ...toReactTextStyle(block.headerStyle),
                display: undefined,
                width: `${column.widthMm}mm`,
                textAlign: alignToCss(column.align),
                ...toReactBorderStyle(column.border ?? block.border),
              }}
            >
              {column.title}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {block.grouping ? (
          <tr className={`new-report-detail-group-row${block.grouping.pageBreakBefore ? " new-report-detail-group-page-break" : ""}`}>
            <td
              colSpan={block.columns.length}
              style={{
                ...toReactTextStyle(block.grouping.style),
                display: undefined,
                ...toReactBorderStyle(block.border),
              }}
            >
              {block.grouping.label}
              {block.grouping.showFieldValue ? (
                <span className="new-report-field-token">{`{{ ${block.grouping.fieldPath} }}`}</span>
              ) : null}
              {block.grouping.pageBreakBefore ? (
                <span className="new-report-detail-group-badge">另起页</span>
              ) : null}
            </td>
          </tr>
        ) : null}
        <tr>
          {block.columns.map((column) => (
            <td
              key={column.id}
              style={{
                ...toReactTextStyle(block.bodyStyle),
                display: undefined,
                textAlign: alignToCss(column.align),
                ...toReactBorderStyle(column.border ?? block.border),
              }}
            >
              {renderDetailCellPreview(column)}
            </td>
          ))}
        </tr>
        {block.grouping?.footer ? (
          <tr className="new-report-detail-group-footer-row">
            <td
              colSpan={Math.min(block.columns.length, Math.max(1, Math.floor(block.grouping.footer.labelColumnSpan)))}
              style={{
                ...toReactTextStyle(block.grouping.footer.style),
                display: undefined,
                textAlign: "right",
                ...toReactBorderStyle(block.border),
              }}
            >
              {block.grouping.footer.label}
            </td>
            {block.columns.slice(Math.min(block.columns.length, Math.max(1, Math.floor(block.grouping.footer.labelColumnSpan)))).map((column) => {
              const cell = block.grouping?.footer?.cells.find((candidate) => candidate.columnId === column.id);

              return (
                <td
                  key={column.id}
                  style={{
                    ...toReactTextStyle(block.grouping!.footer!.style),
                    display: undefined,
                    textAlign: alignToCss(column.align),
                    ...toReactBorderStyle(column.border ?? block.border),
                  }}
                >
                  {cell ? renderGroupFooterCell(block.id, cell) : ""}
                </td>
              );
            })}
          </tr>
        ) : null}
        {block.summaryRow ? (
          <tr className="new-report-detail-summary-row">
            <td
              colSpan={Math.min(block.columns.length, Math.max(1, Math.floor(block.summaryRow.labelColumnSpan)))}
              style={{
                ...toReactTextStyle(block.summaryRow.style),
                display: undefined,
                textAlign: "right",
                ...toReactBorderStyle(block.border),
              }}
            >
              {block.summaryRow.label}
            </td>
            {block.columns.slice(Math.min(block.columns.length, Math.max(1, Math.floor(block.summaryRow.labelColumnSpan)))).map((column) => {
              const cell = block.summaryRow?.cells.find((candidate) => candidate.columnId === column.id);

              return (
                <td
                  key={column.id}
                  style={{
                    ...toReactTextStyle(block.summaryRow!.style),
                    display: undefined,
                    textAlign: alignToCss(column.align),
                    ...toReactBorderStyle(column.border ?? block.border),
                  }}
                >
                  {cell ? renderSummaryCell(cell) : ""}
                </td>
              );
            })}
          </tr>
        ) : null}
      </tbody>
    </table>
  );

  if (!block.sideBand) {
    return table;
  }

  return (
    <table className="new-report-detail-layout">
      <thead>
        <tr>
          <th style={{ width: `${block.sideBand.widthMm}mm`, ...toDetailLayoutBorder(block), ...toReactTextStyle(block.headerStyle), display: undefined }}>
            {block.sideBand.title}
          </th>
          <th style={{ width: block.detailWidthMm ? `${block.detailWidthMm}mm` : undefined, ...toDetailLayoutBorder(block), ...toReactTextStyle(block.headerStyle), display: undefined }}>
            {block.title || "Detail"}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td
            style={{
              ...toDetailLayoutBorder(block),
              width: `${block.sideBand.widthMm}mm`,
              verticalAlign: "top",
              whiteSpace: "pre-wrap",
              overflowWrap: "anywhere",
              wordBreak: "break-word",
              ...toReactTextStyle(block.sideBand.style),
              display: undefined,
            }}
          >
            {block.sideBand.contentKind === "Field" ? (
              <span className="new-report-field-token">{`{{ ${block.sideBand.fieldPath} }}`}</span>
            ) : (
              block.sideBand.text
            )}
          </td>
          <td
            style={{
              ...toDetailLayoutBorder(block),
              width: block.detailWidthMm ? `${block.detailWidthMm}mm` : undefined,
              verticalAlign: "top",
              padding: 0,
            }}
          >
            {table}
          </td>
        </tr>
      </tbody>
    </table>
  );
}

function renderDetailHeaderGroupCells(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const cells = [];
  let index = 0;
  while (index < block.columns.length) {
    const column = block.columns[index];
    const requestedSpan = column.headerGroupTitle ? Math.max(1, Math.floor(column.headerGroupSpan ?? 1)) : 1;
    const columnSpan = Math.min(requestedSpan, block.columns.length - index);
    cells.push(
      <th
        colSpan={columnSpan}
        key={column.id}
        style={{
          ...toReactTextStyle(block.headerStyle),
          display: undefined,
          textAlign: alignToCss(column.align),
          ...toReactBorderStyle(column.border ?? block.border),
        }}
      >
        {column.headerGroupTitle ?? ""}
      </th>,
    );
    index += columnSpan;
  }

  return cells;
}

function renderDetailCellPreview(column: Extract<ReportBlock, { type: "DetailTable" }>["columns"][number]) {
  if (column.contentKind !== "Composite" || !column.content || column.content.length === 0) {
    return column.fieldPath;
  }

  return column.content.map((part) => {
    switch (part.kind) {
      case "Text":
        return <span key={part.id}>{part.text}</span>;
      case "Field":
        return <span className="new-report-field-token" key={part.id}>{`{{ ${part.fieldPath} }}`}</span>;
      case "LineBreak":
        return <br key={part.id} />;
    }
  });
}

function renderSummaryCell(cell: NonNullable<Extract<ReportBlock, { type: "DetailTable" }>["summaryRow"]>["cells"][number]) {
  switch (cell.contentKind) {
    case "Field":
      return <span className="new-report-field-token">{`{{ ${cell.fieldPath} }}`}</span>;
    case "Text":
      return cell.text;
    case "Empty":
      return "";
  }
}

function renderGroupFooterCell(
  blockId: string,
  cell: NonNullable<NonNullable<Extract<ReportBlock, { type: "DetailTable" }>["grouping"]>["footer"]>["cells"][number],
) {
  switch (cell.contentKind) {
    case "Sum":
      return <span className="new-report-field-token">{`sum(${cell.fieldPath})`}</span>;
    case "Count":
      return <span className="new-report-field-token">{`count(${blockId})`}</span>;
    case "Text":
      return cell.text;
    case "Empty":
      return "";
  }
}

function toDetailLayoutBorder(block: Extract<ReportBlock, { type: "DetailTable" }>): CSSProperties {
  return toReactBorderStyle(block.border);
}

function readPageSize(schema: ReportDesignerSchema) {
  const portrait = schema.page.size === "A5"
    ? { widthMm: 148, heightMm: 210 }
    : schema.page.size === "Letter"
      ? { widthMm: 216, heightMm: 279 }
      : schema.page.size === "Custom"
        ? { widthMm: schema.page.widthMm ?? 210, heightMm: schema.page.heightMm ?? 297 }
        : { widthMm: 210, heightMm: 297 };

  return schema.page.orientation === "Landscape"
    ? { widthMm: portrait.heightMm, heightMm: portrait.widthMm }
    : portrait;
}

function toReactTextStyle(style: ReportTextStyle): CSSProperties {
  return {
    display: "block",
    textAlign: style.align ? alignToCss(style.align) : undefined,
    fontSize: style.fontSizePt ? `${style.fontSizePt}pt` : undefined,
    fontWeight: style.bold ? 700 : undefined,
    marginTop: style.marginTopMm ? `${style.marginTopMm}mm` : undefined,
    marginRight: style.marginRightMm ? `${style.marginRightMm}mm` : undefined,
    marginBottom: style.marginBottomMm ? `${style.marginBottomMm}mm` : undefined,
    marginLeft: style.marginLeftMm ? `${style.marginLeftMm}mm` : undefined,
  };
}

function toReactBorderStyle(border?: ReportBorderStyle): CSSProperties {
  if (!border) {
    return {};
  }

  if (border.widthPx <= 0 || border.style === "None") {
    return {
      borderTop: "0",
      borderRight: "0",
      borderBottom: "0",
      borderLeft: "0",
    };
  }

  const line = `${border.widthPx}px ${border.style === "Dashed" ? "dashed" : "solid"} ${border.color}`;
  return {
    borderTop: border.top ? line : "0",
    borderRight: border.right ? line : "0",
    borderBottom: border.bottom ? line : "0",
    borderLeft: border.left ? line : "0",
    padding: border.top || border.right || border.bottom || border.left ? "2mm" : undefined,
  };
}

function alignToCss(value: "Left" | "Center" | "Right") {
  return value.toLowerCase() as CSSProperties["textAlign"];
}
