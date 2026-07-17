export type ReportDesignerReportType = "ExportDocument" | "PaymentVoucher";

export type ReportDesignerSchema = {
  version: 2;
  reportType: ReportDesignerReportType;
  page: ReportPageSettings;
  sections: ReportSection[];
};

export type ReportPageSettings = {
  size: "A4" | "A5" | "Letter" | "Custom";
  orientation: "Portrait" | "Landscape";
  widthMm?: number;
  heightMm?: number;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
  fontFamily: string;
  fontSizePt: number;
};

export type ReportSection = {
  id: string;
  type: "Header" | "Body" | "Footer";
  print: ReportSectionPrintSettings;
  blocks: ReportBlock[];
};

export type ReportSectionPrintSettings = {
  repeatOnEveryPage: boolean;
  keepTogether: boolean;
  pinToPageBottom?: boolean;
  minHeightMm?: number;
};

export type ReportBlockOutputSettings = {
  enabled: boolean;
  note?: string;
};

export type ReportBlockBase = {
  id: string;
  output?: ReportBlockOutputSettings;
};

export type ReportBlock =
  | ReportTextBlock
  | ReportFieldBlock
  | ReportRowBlock
  | ReportGridBlock
  | ReportConditionalBlock
  | ReportImageBlock
  | ReportDetailTableBlock
  | ReportPageBreakBlock;

export type ReportTextBlock = ReportBlockBase & {
  type: "Text";
  text: string;
  style: ReportTextStyle;
  border?: ReportBorderStyle;
};

export type ReportFieldBlock = ReportBlockBase & {
  type: "Field";
  label?: string;
  fieldPath: string;
  fallbackText?: string;
  style: ReportTextStyle;
  border?: ReportBorderStyle;
};

export type ReportRowBlock = ReportBlockBase & {
  type: "Row";
  columns: ReportRowColumn[];
  marginTopMm?: number;
  marginBottomMm?: number;
};

export type ReportRowColumn = {
  id: string;
  contentKind: "Text" | "Field";
  text: string;
  label?: string;
  fieldPath: string;
  fallbackText?: string;
  widthPercent: number;
  style: ReportTextStyle;
  border?: ReportBorderStyle;
};

export type ReportGridBlock = ReportBlockBase & {
  type: "Grid";
  title?: string;
  columns: ReportGridColumn[];
  rows: ReportGridRow[];
  marginTopMm?: number;
  marginBottomMm?: number;
  border: ReportBorderStyle;
  defaultCellStyle: ReportTextStyle;
};

export type ReportGridColumn = {
  id: string;
  widthPercent: number;
};

export type ReportGridRow = {
  id: string;
  heightMm?: number;
  cells: ReportGridCell[];
};

export type ReportGridCell = {
  id: string;
  colSpan?: number;
  rowSpan?: number;
  contentKind: "Text" | "Field" | "CheckboxGroup";
  text: string;
  label?: string;
  fieldPath: string;
  fallbackText?: string;
  checkboxOptions?: ReportGridCheckboxOption[];
  verticalText?: boolean;
  style: ReportTextStyle;
  border?: ReportBorderStyle;
};

export type ReportGridCheckboxOption = {
  id: string;
  label: string;
  value: string;
};

export type ReportConditionalBlock = ReportBlockBase & {
  type: "Conditional";
  condition: ReportConditionalRule;
  content: ReportConditionalContent;
  style: ReportTextStyle;
  border?: ReportBorderStyle;
};

export type ReportConditionalRule = {
  fieldPath: string;
  operator: "HasValue" | "Equals" | "NotEquals";
  value: string;
};

export type ReportConditionalContent = {
  kind: "Text" | "Field";
  text: string;
  label?: string;
  fieldPath: string;
  fallbackText?: string;
};

export type ReportImageBlock = ReportBlockBase & {
  type: "Image";
  title?: string;
  sourceKind: "Field" | "StaticUrl";
  fieldPath: string;
  url: string;
  altText?: string;
  widthMm: number;
  heightMm?: number;
  align: "Left" | "Center" | "Right";
  marginTopMm?: number;
  marginBottomMm?: number;
  hideWhenSourceEmpty: boolean;
  keepTogether: boolean;
};

export type ReportDetailTableBlock = ReportBlockBase & {
  type: "DetailTable";
  title?: string;
  detailWidthMm?: number;
  sourcePath: "Invoice.Items";
  repeatMode: "ScribanFor";
  print: ReportDetailTablePrintSettings;
  sideBand?: ReportDetailTableSideBand;
  grouping?: ReportDetailTableGrouping;
  columns: ReportDetailTableColumn[];
  summaryRow?: ReportDetailTableSummaryRow;
  headerStyle: ReportTextStyle;
  bodyStyle: ReportTextStyle;
  border: ReportBorderStyle;
};

export type ReportDetailTablePrintSettings = {
  repeatHeaderOnPageBreak: boolean;
  keepRowsTogether: boolean;
};

export type ReportDetailTableSideBand = {
  title: string;
  widthMm: number;
  contentKind: "Text" | "Field";
  text: string;
  fieldPath: string;
  style: ReportTextStyle;
};

export type ReportDetailTableGrouping = {
  fieldPath: string;
  label: string;
  showFieldValue: boolean;
  keepTogether: boolean;
  pageBreakBefore?: boolean;
  footer?: ReportDetailTableGroupFooter;
  style: ReportTextStyle;
};

export type ReportDetailTableGroupFooter = {
  label: string;
  labelColumnSpan: number;
  cells: ReportDetailTableGroupFooterCell[];
  style: ReportTextStyle;
};

export type ReportDetailTableGroupFooterCell = {
  columnId: string;
  contentKind: "Empty" | "Text" | "Sum" | "Count";
  text: string;
  fieldPath: string;
};

export type ReportDetailTableColumn = {
  id: string;
  title: string;
  headerGroupTitle?: string;
  headerGroupSpan?: number;
  contentKind?: "Field" | "Composite";
  fieldPath: string;
  content?: ReportDetailTableCellContent[];
  widthMm: number;
  align: "Left" | "Center" | "Right";
  format?: string;
  border?: ReportBorderStyle;
};

export type ReportDetailTableCellContent = {
  id: string;
  kind: "Text" | "Field" | "LineBreak";
  text: string;
  fieldPath: string;
};

export type ReportDetailTableSummaryRow = {
  label: string;
  labelColumnSpan: number;
  cells: ReportDetailTableSummaryCell[];
  style: ReportTextStyle;
};

export type ReportDetailTableSummaryCell = {
  columnId: string;
  contentKind: "Empty" | "Text" | "Field";
  text: string;
  fieldPath: string;
};

export type ReportPageBreakBlock = ReportBlockBase & {
  type: "PageBreak";
};

export type ReportTextStyle = {
  fontSizePt?: number;
  bold?: boolean;
  align?: "Left" | "Center" | "Right";
  marginTopMm?: number;
  marginRightMm?: number;
  marginBottomMm?: number;
  marginLeftMm?: number;
};

export type ReportBorderStyle = {
  color: string;
  widthPx: number;
  style?: "Solid" | "Dashed" | "None";
  top?: boolean;
  right?: boolean;
  bottom?: boolean;
  left?: boolean;
};
