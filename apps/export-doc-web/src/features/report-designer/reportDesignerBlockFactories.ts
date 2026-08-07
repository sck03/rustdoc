import type {
  ReportBlock,
  ReportBorderStyle,
  ReportDesignerReportType,
  ReportDetailTableCellContent,
  ReportDetailTableColumn,
  ReportDetailTableGroupFooter,
  ReportDetailTableGrouping,
  ReportDetailTableSummaryRow,
  ReportGridCell,
  ReportGridColumn,
  ReportGridRow,
  ReportRowColumn,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";
import {
  createReportBlockId,
  normalizeDesignerFieldPath,
} from "./reportDesignerMutationUtils.ts";

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
