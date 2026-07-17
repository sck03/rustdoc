import type { ReportDesignerDocumentState } from "./reportDesignerHistory.ts";
import type {
  ReportBlock,
  ReportBorderStyle,
  ReportDesignerReportType,
  ReportDesignerSchema,
  ReportDetailTableBlock,
  ReportDetailTableCellContent,
  ReportDetailTableColumn,
  ReportDetailTableGroupFooter,
  ReportDetailTableGroupFooterCell,
  ReportDetailTableGrouping,
  ReportDetailTableSummaryCell,
  ReportDetailTableSummaryRow,
  ReportGridBlock,
  ReportGridCell,
  ReportGridColumn,
  ReportGridRow,
  ReportRowColumn,
  ReportSection,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";
import {
  findFirstSectionAllowingBlock,
  isReportDesignerBlockAllowedInSection,
} from "./reportDesignerModel.ts";
import { findSelectedBlock, findSelectedSection } from "./reportDesignerSelection.ts";

const defaultTextStyle: ReportTextStyle = {
  fontSizePt: 10,
  align: "Left",
  marginTopMm: 1.5,
  marginBottomMm: 1.5,
};

const defaultBorderStyle: ReportBorderStyle = {
  color: "#333333",
  widthPx: 0,
  style: "Solid",
  top: false,
  right: false,
  bottom: false,
  left: false,
};

const defaultTableBorderStyle: ReportBorderStyle = {
  color: "#333333",
  widthPx: 1,
  style: "Solid",
  top: true,
  right: true,
  bottom: true,
  left: true,
};

export type ReportDesignerBlockDropTarget = {
  sectionId: string;
  blockId?: string;
  placement: "before" | "after" | "inside";
};

export function createTextBlock(text = "New text"): ReportBlock {
  return {
    id: createReportBlockId("text"),
    type: "Text",
    text,
    style: defaultTextStyle,
    border: defaultBorderStyle,
  };
}

export function createFieldBlock(label: string, fieldPath: string): ReportBlock {
  return {
    id: createReportBlockId("field"),
    type: "Field",
    label,
    fieldPath: normalizeDesignerFieldPath(fieldPath),
    fallbackText: "",
    style: defaultTextStyle,
    border: defaultBorderStyle,
  };
}

export function createRowBlock(reportType: ReportDesignerReportType = "ExportDocument"): ReportBlock {
  if (reportType === "PaymentVoucher") {
    return {
      id: createReportBlockId("row"),
      type: "Row",
      columns: [
        createRowColumn("Field", "", "Payment.Project", 50, {
          ...defaultTextStyle,
          bold: true,
          align: "Left",
        }, "项目/业务号"),
        createRowColumn("Field", "", "Payment.PaymentDate", 50, {
          ...defaultTextStyle,
          align: "Right",
        }, "申请日期"),
      ],
      marginTopMm: 1.5,
      marginBottomMm: 1.5,
    };
  }

  return {
    id: createReportBlockId("row"),
    type: "Row",
    columns: [
      createRowColumn("Text", "TO:M/S\nCustomer name and address", "", 58, {
        ...defaultTextStyle,
        bold: true,
        align: "Left",
      }),
      createRowColumn("Field", "", "Invoice.InvoiceNo", 42, {
        ...defaultTextStyle,
        bold: true,
        align: "Right",
      }, "Invoice No."),
    ],
    marginTopMm: 1.5,
    marginBottomMm: 1.5,
  };
}

export function createGridBlock(reportType: ReportDesignerReportType = "ExportDocument"): ReportBlock {
  const primaryFieldPath = reportType === "PaymentVoucher" ? "Payment.InvoiceNo" : "Invoice.InvoiceNo";
  const secondaryFieldPath = reportType === "PaymentVoucher" ? "Payment.PayeeName" : "Customer.CustomerNameEN";

  return {
    id: createReportBlockId("grid"),
    type: "Grid",
    title: "固定票据表格",
    columns: createGridColumns(4),
    rows: [
      createGridRow([
        createGridCell("Text", "标签", "", 1, 1, { ...defaultTextStyle, bold: true, align: "Center" }),
        createGridCell("Field", "", primaryFieldPath, 1, 1, { ...defaultTextStyle, align: "Center" }),
        createGridCell("Text", "标签", "", 1, 1, { ...defaultTextStyle, bold: true, align: "Center" }),
        createGridCell("Field", "", secondaryFieldPath, 1, 1, { ...defaultTextStyle, align: "Center" }),
      ]),
      createGridRow([
        createGridCell("Text", "合并标签", "", 1, 1, { ...defaultTextStyle, bold: true, align: "Center" }),
        createGridCell("Text", "可在属性面板改为字段、勾选组、竖排文字或继续拆分单元格", "", 3, 1, { ...defaultTextStyle, align: "Left" }),
      ]),
    ],
    marginTopMm: 2,
    marginBottomMm: 2,
    border: defaultTableBorderStyle,
    defaultCellStyle: defaultTextStyle,
  };
}

export function createConditionalBlock(reportType: ReportDesignerReportType = "ExportDocument"): ReportBlock {
  const fieldPath = reportType === "PaymentVoucher" ? "Payment.Notes" : "Invoice.SpecialTerms";
  const label = reportType === "PaymentVoucher" ? "备注" : "Special Terms";

  return {
    id: createReportBlockId("conditional"),
    type: "Conditional",
    condition: {
      fieldPath,
      operator: "HasValue",
      value: "",
    },
    content: {
      kind: "Field",
      label,
      fieldPath,
      fallbackText: "",
      text: "",
    },
    style: defaultTextStyle,
    border: defaultBorderStyle,
  };
}

export function createImageBlock(): ReportBlock {
  return {
    id: createReportBlockId("image"),
    type: "Image",
    title: "Document seal",
    sourceKind: "Field",
    fieldPath: "doc_seal_path",
    url: "",
    altText: "Document seal",
    widthMm: 42,
    align: "Right",
    marginTopMm: 4,
    marginBottomMm: 2,
    hideWhenSourceEmpty: true,
    keepTogether: true,
  };
}

export function createPageBreakBlock(): ReportBlock {
  return {
    id: createReportBlockId("page-break"),
    type: "PageBreak",
  };
}

export function createDetailTableBlock(): ReportBlock {
  const columns = [
    createDetailTableColumn("Product", "Invoice.Items.ProductNameEN", 72, "Left"),
    createDetailTableColumn("Quantity", "Invoice.Items.Quantity", 24, "Right"),
    createDetailTableColumn("Unit Price", "Invoice.Items.UnitPrice", 28, "Right"),
    createDetailTableColumn("Amount", "Invoice.Items.TotalPrice", 30, "Right"),
  ];

  return {
    id: createReportBlockId("detail-table"),
    type: "DetailTable",
    title: "Quantities and Descriptions",
    detailWidthMm: 132,
    sourcePath: "Invoice.Items",
    repeatMode: "ScribanFor",
    print: createDetailTablePrintSettings(),
    columns,
    summaryRow: createDetailTableSummaryRow(columns),
    headerStyle: {
      ...defaultTextStyle,
      bold: true,
      align: "Center",
    },
    bodyStyle: defaultTextStyle,
    border: defaultTableBorderStyle,
  };
}

export function createDetailTablePrintSettings() {
  return {
    repeatHeaderOnPageBreak: true,
    keepRowsTogether: true,
  };
}

export function createDetailTableSummaryRow(columns: ReportDetailTableColumn[]): ReportDetailTableSummaryRow {
  const valueColumn = columns[columns.length - 1];

  return {
    label: "TOTAL",
    labelColumnSpan: Math.max(1, columns.length - 1),
    cells: valueColumn
      ? [
          {
            columnId: valueColumn.id,
            contentKind: "Field",
            text: "",
            fieldPath: "Invoice.TotalAmount",
          },
        ]
      : [],
    style: {
      ...defaultTextStyle,
      bold: true,
      align: "Right",
    },
  };
}

export function createDetailTableSideBand() {
  return {
    title: "唛头 Marks",
    widthMm: 36,
    contentKind: "Field" as const,
    text: "Vendor name:\nOrder number:\nDescription:\nSKU NO:\nColour:\nSIZE:\nCarton Number:\nDimension(CM):\nGross Weight:\nBATCH Number:\nCountry of Origin: China",
    fieldPath: "Invoice.ShippingMarks",
    style: {
      ...defaultTextStyle,
      fontSizePt: 10,
      bold: true,
      marginTopMm: 18,
    },
  };
}

export function createDetailTableGrouping(
  fieldPath = "Invoice.Items.ProductNameEN",
  label = "Group",
): ReportDetailTableGrouping {
  return {
    fieldPath: normalizeDesignerFieldPath(fieldPath),
    label,
    showFieldValue: true,
    keepTogether: true,
    pageBreakBefore: false,
    style: {
      ...defaultTextStyle,
      bold: true,
      align: "Left",
      marginTopMm: 0,
      marginBottomMm: 0,
    },
  };
}

export function createDetailTableGroupFooter(columns: ReportDetailTableColumn[]): ReportDetailTableGroupFooter {
  const valueColumn = columns[columns.length - 1];

  return {
    label: "SUBTOTAL",
    labelColumnSpan: Math.max(1, columns.length - 1),
    cells: valueColumn
      ? [
          {
            columnId: valueColumn.id,
            contentKind: "Sum",
            text: "",
            fieldPath: valueColumn.fieldPath,
          },
        ]
      : [],
    style: {
      ...defaultTextStyle,
      bold: true,
      align: "Right",
    },
  };
}

export function createRowColumn(
  contentKind: "Text" | "Field" = "Text",
  text = "Text",
  fieldPath = "",
  widthPercent = 50,
  style: ReportTextStyle = defaultTextStyle,
  label = "",
): ReportRowColumn {
  return {
    id: createReportBlockId("row-col"),
    contentKind,
    text,
    label,
    fieldPath: normalizeDesignerFieldPath(fieldPath),
    fallbackText: "",
    widthPercent,
    style,
    border: defaultBorderStyle,
  };
}

export function createDetailTableColumn(
  title = "Column",
  fieldPath = "Invoice.Items.ProductNameEN",
  widthMm = 30,
  align: "Left" | "Center" | "Right" = "Left",
) {
  return {
    id: createReportBlockId("detail-col"),
    title,
    contentKind: "Field" as const,
    fieldPath: normalizeDesignerFieldPath(fieldPath),
    content: [],
    widthMm,
    align,
  };
}

export function createDetailTableCellContent(kind: "Text" | "Field" | "LineBreak" = "Field"): ReportDetailTableCellContent {
  return {
    id: createReportBlockId("detail-cell-part"),
    kind,
    text: kind === "Text" ? "Text" : "",
    fieldPath: kind === "Field" ? "Invoice.Items.ProductNameEN" : "",
  };
}

export function createGridColumns(count: number): ReportGridColumn[] {
  const safeCount = Math.max(1, Math.floor(count));
  const widthPercent = Math.round((1000 / safeCount)) / 10;
  return Array.from({ length: safeCount }, (_, index) => ({
    id: createReportBlockId(`grid-col-${index + 1}`),
    widthPercent,
  }));
}

export function createGridRow(cells?: ReportGridCell[], heightMm = 9): ReportGridRow {
  return {
    id: createReportBlockId("grid-row"),
    heightMm,
    cells: cells ?? [createGridCell()],
  };
}

export function createGridCell(
  contentKind: ReportGridCell["contentKind"] = "Text",
  text = "Text",
  fieldPath = "",
  colSpan = 1,
  rowSpan = 1,
  style: ReportTextStyle = defaultTextStyle,
  checkboxOptions?: ReportGridCell["checkboxOptions"],
  verticalText = false,
): ReportGridCell {
  return {
    id: createReportBlockId("grid-cell"),
    colSpan,
    rowSpan,
    contentKind,
    text,
    label: "",
    fieldPath: normalizeDesignerFieldPath(fieldPath),
    fallbackText: "",
    checkboxOptions: checkboxOptions ?? [],
    verticalText,
    style,
    border: defaultTableBorderStyle,
  };
}

export function normalizeRowColumnWidths(columns: ReportRowColumn[]) {
  const safeColumns = columns.length > 0 ? columns : [createRowColumn()];
  const total = safeColumns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  return safeColumns.map((column) => ({
    ...column,
    widthPercent: Math.round((Math.max(1, column.widthPercent) / total) * 1000) / 10,
  }));
}

export function distributeGridColumnWidths(block: ReportGridBlock): ReportGridBlock {
  const safeColumns = block.columns.length > 0 ? block.columns : createGridColumns(1);
  const widthPercent = Math.round((1000 / safeColumns.length)) / 10;

  return {
    ...block,
    columns: safeColumns.map((column) => ({
      ...column,
      widthPercent,
    })),
  };
}

export function resizeAdjacentGridColumnWidths(
  block: ReportGridBlock,
  leftColumnId: string,
  deltaPercent: number,
): ReportGridBlock {
  return {
    ...block,
    columns: resizeAdjacentWidths(block.columns, leftColumnId, deltaPercent, 1, "widthPercent"),
  };
}

export function resizeAdjacentRowColumnWidths(
  columns: ReportRowColumn[],
  leftColumnId: string,
  deltaPercent: number,
): ReportRowColumn[] {
  return normalizeRowColumnWidths(resizeAdjacentWidths(columns, leftColumnId, deltaPercent, 1, "widthPercent"));
}

export function resizeAdjacentDetailTableColumnWidths(
  block: ReportDetailTableBlock,
  leftColumnId: string,
  deltaMm: number,
): ReportDetailTableBlock {
  return {
    ...block,
    columns: resizeAdjacentWidths(block.columns, leftColumnId, deltaMm, 8, "widthMm"),
  };
}

export function applyGridDefaultCellStyle(block: ReportGridBlock): ReportGridBlock {
  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.map((cell) => ({
        ...cell,
        style: { ...block.defaultCellStyle },
      })),
    })),
  };
}

export function applyGridBorderToCells(block: ReportGridBlock): ReportGridBlock {
  return {
    ...block,
    rows: block.rows.map((row) => ({
      ...row,
      cells: row.cells.map((cell) => ({
        ...cell,
        border: { ...block.border },
      })),
    })),
  };
}

export function distributeDetailTableColumnWidths(block: ReportDetailTableBlock): ReportDetailTableBlock {
  if (block.columns.length === 0) {
    return block;
  }

  const totalWidthMm = block.columns.reduce((sum, column) => sum + Math.max(8, column.widthMm), 0);
  const widthMm = Math.round((totalWidthMm / block.columns.length) * 10) / 10;

  return {
    ...block,
    columns: block.columns.map((column) => ({
      ...column,
      widthMm,
    })),
  };
}

export function applyDetailTableBorderToColumns(block: ReportDetailTableBlock): ReportDetailTableBlock {
  return {
    ...block,
    columns: block.columns.map((column) => ({
      ...column,
      border: { ...block.border },
    })),
  };
}

export function clearDetailTableColumnBorders(block: ReportDetailTableBlock): ReportDetailTableBlock {
  return {
    ...block,
    columns: block.columns.map((column) => {
      const { border: _border, ...nextColumn } = column;
      return nextColumn;
    }),
  };
}

export function moveDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
  direction: "up" | "down",
): ReportDetailTableBlock {
  const currentIndex = block.columns.findIndex((column) => column.id === columnId);
  const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
  if (currentIndex < 0 || targetIndex < 0 || targetIndex >= block.columns.length) {
    return block;
  }

  const columns = [...block.columns];
  const [movedColumn] = columns.splice(currentIndex, 1);
  columns.splice(targetIndex, 0, movedColumn);

  return {
    ...block,
    columns,
  };
}

export function reorderDetailTableColumn(
  block: ReportDetailTableBlock,
  sourceColumnId: string,
  targetColumnId: string,
): ReportDetailTableBlock {
  if (sourceColumnId === targetColumnId) {
    return block;
  }

  const sourceIndex = block.columns.findIndex((column) => column.id === sourceColumnId);
  const targetIndex = block.columns.findIndex((column) => column.id === targetColumnId);
  if (sourceIndex < 0 || targetIndex < 0) {
    return block;
  }

  const columns = [...block.columns];
  const [movedColumn] = columns.splice(sourceIndex, 1);
  columns.splice(targetIndex, 0, movedColumn);

  return {
    ...block,
    columns,
  };
}

export function duplicateDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
): ReportDetailTableBlock {
  const currentIndex = block.columns.findIndex((column) => column.id === columnId);
  const sourceColumn = block.columns[currentIndex];
  if (!sourceColumn) {
    return block;
  }

  const duplicate = {
    ...createDetailTableColumn(
      `${sourceColumn.title} Copy`,
      sourceColumn.fieldPath,
      sourceColumn.widthMm,
      sourceColumn.align,
    ),
    headerGroupTitle: sourceColumn.headerGroupTitle,
    headerGroupSpan: sourceColumn.headerGroupSpan,
    contentKind: sourceColumn.contentKind,
    content: sourceColumn.content?.map((part) => ({ ...part, id: createReportBlockId("detail-cell-part") })),
    format: sourceColumn.format,
    border: sourceColumn.border,
  };
  const columns = [
    ...block.columns.slice(0, currentIndex + 1),
    duplicate,
    ...block.columns.slice(currentIndex + 1),
  ];

  return {
    ...block,
    columns,
    summaryRow: block.summaryRow
      ? {
          ...block.summaryRow,
          cells: [
            ...block.summaryRow.cells,
            createEmptySummaryCell(duplicate.id),
          ],
        }
      : undefined,
    grouping: block.grouping
      ? {
          ...block.grouping,
          footer: block.grouping.footer
            ? {
                ...block.grouping.footer,
                cells: [
                  ...block.grouping.footer.cells,
                  createEmptyGroupFooterCell(duplicate.id),
                ],
              }
            : undefined,
        }
      : undefined,
  };
}

export function removeDetailTableColumn(
  block: ReportDetailTableBlock,
  columnId: string,
): ReportDetailTableBlock {
  if (block.columns.length <= 1) {
    return block;
  }

  const columns = block.columns.filter((column) => column.id !== columnId);

  return {
    ...block,
    columns,
    summaryRow: block.summaryRow
      ? {
          ...block.summaryRow,
          labelColumnSpan: Math.min(block.summaryRow.labelColumnSpan, columns.length),
          cells: block.summaryRow.cells.filter((cell) => columns.some((column) => column.id === cell.columnId)),
        }
      : undefined,
    grouping: block.grouping
      ? {
          ...block.grouping,
          footer: block.grouping.footer
            ? {
                ...block.grouping.footer,
                labelColumnSpan: Math.min(block.grouping.footer.labelColumnSpan, columns.length),
                cells: block.grouping.footer.cells.filter((cell) => columns.some((column) => column.id === cell.columnId)),
              }
            : undefined,
        }
      : undefined,
  };
}

export function insertBlockAfterSelection(
  documentState: ReportDesignerDocumentState,
  block: ReportBlock,
): ReportDesignerDocumentState {
  const selected = findSelectedBlock(documentState.schema, documentState.selectedBlockId);
  const selectedSection = selected
    ? selected.section
    : findSelectedSection(documentState.schema, documentState.selectedSectionId);
  const requestedSection = selectedSection ?? findDefaultSection(documentState.schema);
  const targetSection = isReportDesignerBlockAllowedInSection(block, requestedSection)
    ? requestedSection
    : findFirstSectionAllowingBlock(documentState.schema, block) ?? requestedSection;
  const targetSectionId = targetSection.id;

  return {
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => {
        if (section.id !== targetSectionId) {
          return section;
        }

        const selectedIndex = selected && selected.section.id === section.id
          ? section.blocks.findIndex((candidate) => candidate.id === selected.block.id)
          : -1;
        const insertIndex = selectedIndex >= 0 ? selectedIndex + 1 : section.blocks.length;

        return {
          ...section,
          blocks: [
            ...section.blocks.slice(0, insertIndex),
            block,
            ...section.blocks.slice(insertIndex),
          ],
        };
      }),
    },
    selectedBlockId: block.id,
    selectedSectionId: null,
  };
}

export function insertBlockAtDropTarget(
  documentState: ReportDesignerDocumentState,
  block: ReportBlock,
  target: ReportDesignerBlockDropTarget,
): ReportDesignerDocumentState {
  const requestedSection = documentState.schema.sections.find((section) => section.id === target.sectionId);
  const targetSection = requestedSection && isReportDesignerBlockAllowedInSection(block, requestedSection)
    ? requestedSection
    : findFirstSectionAllowingBlock(documentState.schema, block);
  if (!targetSection) {
    return documentState;
  }

  const normalizedTarget = targetSection.id === target.sectionId
    ? target
    : { sectionId: targetSection.id, placement: "inside" as const };

  return {
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => {
        if (section.id !== normalizedTarget.sectionId) {
          return section;
        }

        const insertIndex = resolveDropInsertIndex(section, normalizedTarget);
        return {
          ...section,
          blocks: [
            ...section.blocks.slice(0, insertIndex),
            block,
            ...section.blocks.slice(insertIndex),
          ],
        };
      }),
    },
    selectedBlockId: block.id,
    selectedSectionId: null,
  };
}

export function moveBlockToDropTarget(
  documentState: ReportDesignerDocumentState,
  blockId: string,
  target: ReportDesignerBlockDropTarget,
): ReportDesignerDocumentState {
  let movingBlock: ReportBlock | null = null;
  let sourceSectionId = "";
  let sourceIndex = -1;
  let targetIndex = -1;

  for (const section of documentState.schema.sections) {
    const blockIndex = section.blocks.findIndex((block) => block.id === blockId);
    if (blockIndex >= 0) {
      movingBlock = section.blocks[blockIndex];
      sourceSectionId = section.id;
      sourceIndex = blockIndex;
    }

    if (section.id === target.sectionId) {
      targetIndex = resolveDropInsertIndex(section, target);
    }
  }

  const targetSection = documentState.schema.sections.find((section) => section.id === target.sectionId);
  if (!movingBlock || !sourceSectionId || sourceIndex < 0 || targetIndex < 0 || !targetSection) {
    return documentState;
  }

  if (!isReportDesignerBlockAllowedInSection(movingBlock, targetSection)) {
    return {
      ...documentState,
      selectedBlockId: movingBlock.id,
      selectedSectionId: null,
    };
  }

  const adjustedTargetIndex = sourceSectionId === target.sectionId && sourceIndex < targetIndex
    ? targetIndex - 1
    : targetIndex;

  if (sourceSectionId === target.sectionId && sourceIndex === adjustedTargetIndex) {
    return {
      ...documentState,
      selectedBlockId: movingBlock.id,
      selectedSectionId: null,
    };
  }

  return {
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => {
        if (section.id !== sourceSectionId && section.id !== target.sectionId) {
          return section;
        }

        const withoutMovingBlock = section.id === sourceSectionId
          ? section.blocks.filter((block) => block.id !== movingBlock.id)
          : section.blocks;

        if (section.id !== target.sectionId) {
          return {
            ...section,
            blocks: withoutMovingBlock,
          };
        }

        return {
          ...section,
          blocks: [
            ...withoutMovingBlock.slice(0, adjustedTargetIndex),
            movingBlock,
            ...withoutMovingBlock.slice(adjustedTargetIndex),
          ],
        };
      }),
    },
    selectedBlockId: movingBlock.id,
    selectedSectionId: null,
  };
}

export function updateSelectedBlock(
  documentState: ReportDesignerDocumentState,
  update: (block: ReportBlock) => ReportBlock,
): ReportDesignerDocumentState {
  if (!documentState.selectedBlockId) {
    return documentState;
  }

  return {
    ...documentState,
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => ({
        ...section,
        blocks: section.blocks.map((block) =>
          block.id === documentState.selectedBlockId ? update(block) : block,
        ),
      })),
    },
  };
}

export function removeSelectedBlock(documentState: ReportDesignerDocumentState): ReportDesignerDocumentState {
  if (!documentState.selectedBlockId) {
    return documentState;
  }

  const nextSections = documentState.schema.sections.map((section) => ({
    ...section,
    blocks: section.blocks.filter((block) => block.id !== documentState.selectedBlockId),
  }));
  const allBlocks = nextSections.flatMap((section) => section.blocks);

  return {
    schema: {
      ...documentState.schema,
      sections: nextSections,
    },
    selectedBlockId: allBlocks[0]?.id ?? null,
    selectedSectionId: null,
  };
}

export function moveSelectedBlock(
  documentState: ReportDesignerDocumentState,
  direction: "up" | "down",
): ReportDesignerDocumentState {
  const selected = findSelectedBlock(documentState.schema, documentState.selectedBlockId);
  if (!selected) {
    return documentState;
  }

  const currentIndex = selected.section.blocks.findIndex((block) => block.id === selected.block.id);
  const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
  if (currentIndex < 0 || targetIndex < 0 || targetIndex >= selected.section.blocks.length) {
    return documentState;
  }

  return {
    ...documentState,
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => {
        if (section.id !== selected.section.id) {
          return section;
        }

        const blocks = [...section.blocks];
        const [movedBlock] = blocks.splice(currentIndex, 1);
        blocks.splice(targetIndex, 0, movedBlock);

        return {
          ...section,
          blocks,
        };
      }),
    },
  };
}

export function duplicateSelectedBlock(documentState: ReportDesignerDocumentState): ReportDesignerDocumentState {
  const selected = findSelectedBlock(documentState.schema, documentState.selectedBlockId);
  if (!selected) {
    return documentState;
  }

  const duplicate = cloneBlockWithFreshIds(selected.block);

  return {
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) => {
        if (section.id !== selected.section.id) {
          return section;
        }

        const selectedIndex = section.blocks.findIndex((block) => block.id === selected.block.id);
        const insertIndex = selectedIndex >= 0 ? selectedIndex + 1 : section.blocks.length;

        return {
          ...section,
          blocks: [
            ...section.blocks.slice(0, insertIndex),
            duplicate,
            ...section.blocks.slice(insertIndex),
          ],
        };
      }),
    },
    selectedBlockId: duplicate.id,
    selectedSectionId: null,
  };
}

export function normalizeDesignerFieldPath(value: string) {
  const trimmed = value.trim();
  const scribanMatch = trimmed.match(/^\{\{\s*([^|}]+?)(?:\s*\|[^}]*)?\s*\}\}$/);
  return (scribanMatch?.[1] ?? trimmed).trim();
}

function findDefaultSection(schema: ReportDesignerSchema): ReportSection {
  const section = schema.sections.find((candidate) => candidate.type === "Body") ?? schema.sections[0];
  if (!section) {
    throw new Error("Report designer schema must contain at least one section.");
  }

  return section;
}

function resolveDropInsertIndex(section: ReportSection, target: ReportDesignerBlockDropTarget) {
  if (!target.blockId || target.placement === "inside") {
    return section.blocks.length;
  }

  const targetBlockIndex = section.blocks.findIndex((block) => block.id === target.blockId);
  if (targetBlockIndex < 0) {
    return section.blocks.length;
  }

  return target.placement === "before" ? targetBlockIndex : targetBlockIndex + 1;
}

function createReportBlockId(prefix: string) {
  return `block-${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function cloneBlockWithFreshIds(block: ReportBlock): ReportBlock {
  switch (block.type) {
    case "Text":
      return {
        ...block,
        id: createReportBlockId("text"),
      };
    case "Field":
      return {
        ...block,
        id: createReportBlockId("field"),
      };
    case "Row":
      return {
        ...block,
        id: createReportBlockId("row"),
        columns: block.columns.map((column) => ({
          ...column,
          id: createReportBlockId("row-col"),
        })),
      };
    case "Grid":
      return {
        ...block,
        id: createReportBlockId("grid"),
        columns: block.columns.map((column) => ({
          ...column,
          id: createReportBlockId("grid-col"),
        })),
        rows: block.rows.map((row) => ({
          ...row,
          id: createReportBlockId("grid-row"),
          cells: row.cells.map((cell) => ({
            ...cell,
            id: createReportBlockId("grid-cell"),
            checkboxOptions: cell.checkboxOptions?.map((option) => ({
              ...option,
              id: createReportBlockId("grid-option"),
            })),
          })),
        })),
      };
    case "Conditional":
      return {
        ...block,
        id: createReportBlockId("conditional"),
      };
    case "Image":
      return {
        ...block,
        id: createReportBlockId("image"),
      };
    case "PageBreak":
      return createPageBreakBlock();
    case "DetailTable":
      const clonedColumns = block.columns.map((column) => ({
        ...column,
        id: createReportBlockId("detail-col"),
        content: column.content?.map((part) => ({
          ...part,
          id: createReportBlockId("detail-cell-part"),
        })),
      }));
      const columnIdMap = new Map(block.columns.map((column, index) => [column.id, clonedColumns[index]?.id ?? column.id]));

      return {
        ...block,
        id: createReportBlockId("detail-table"),
        columns: clonedColumns,
        grouping: block.grouping
          ? {
              ...block.grouping,
              footer: block.grouping.footer
                ? {
                    ...block.grouping.footer,
                    cells: block.grouping.footer.cells.map((cell) => ({
                      ...cell,
                      columnId: columnIdMap.get(cell.columnId) ?? cell.columnId,
                    })),
                  }
                : undefined,
            }
          : undefined,
        summaryRow: block.summaryRow
          ? {
              ...block.summaryRow,
              cells: block.summaryRow.cells.map((cell) => ({
                ...cell,
                columnId: columnIdMap.get(cell.columnId) ?? cell.columnId,
              })),
            }
          : undefined,
      };
  }
}

function createEmptySummaryCell(columnId: string): ReportDetailTableSummaryCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

function createEmptyGroupFooterCell(columnId: string): ReportDetailTableGroupFooterCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

function resizeAdjacentWidths<T extends { id: string } & Record<TKey, number>, TKey extends keyof T & string>(
  columns: T[],
  leftColumnId: string,
  delta: number,
  minWidth: number,
  widthKey: TKey,
): T[] {
  if (!Number.isFinite(delta) || columns.length < 2) {
    return columns;
  }

  const leftIndex = columns.findIndex((column) => column.id === leftColumnId);
  const rightIndex = leftIndex + 1;
  const leftColumn = columns[leftIndex];
  const rightColumn = columns[rightIndex];
  if (!leftColumn || !rightColumn) {
    return columns;
  }

  const leftWidth = Math.max(minWidth, Number(leftColumn[widthKey]));
  const rightWidth = Math.max(minWidth, Number(rightColumn[widthKey]));
  const pairTotal = leftWidth + rightWidth;
  if (pairTotal < minWidth * 2) {
    return columns;
  }

  const nextLeftWidth = roundDesignerWidth(clamp(leftWidth + delta, minWidth, pairTotal - minWidth));
  const nextRightWidth = roundDesignerWidth(pairTotal - nextLeftWidth);

  return columns.map((column, index) => {
    if (index === leftIndex) {
      return {
        ...column,
        [widthKey]: nextLeftWidth,
      };
    }

    if (index === rightIndex) {
      return {
        ...column,
        [widthKey]: nextRightWidth,
      };
    }

    return column;
  });
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function roundDesignerWidth(value: number) {
  return Math.round(value * 10) / 10;
}
