import type {
  ReportDesignerReportType,
  ReportDesignerSchema,
} from "./reportDesignerSchema.ts";
import { getReportDesignerBlockPlacementIssue } from "./reportDesignerModel.ts";
import {
  createIssue,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

/**
 * Image bindings are deliberately narrower than ordinary field bindings.
 * These fields are materialized by the server as data URIs; accepting an
 * arbitrary business string here would turn the generated <img> into a
 * browser-side URL fetch primitive (SSRF and data exfiltration risk).
 */
export const CONTROLLED_REPORT_IMAGE_FIELD_PATHS = [
  "doc_seal_path",
  "customs_seal_path",
  "shipping_marks_image_data",
] as const;

export function isControlledReportImageFieldPath(fieldPath: string | undefined): fieldPath is typeof CONTROLLED_REPORT_IMAGE_FIELD_PATHS[number] {
  const normalized = typeof fieldPath === "string" ? fieldPath.trim() : "";
  return CONTROLLED_REPORT_IMAGE_FIELD_PATHS.some((candidate) => candidate === normalized);
}

export function getControlledReportImageFieldPaths(reportType: ReportDesignerReportType): readonly string[] {
  return reportType === "ExportDocument" ? CONTROLLED_REPORT_IMAGE_FIELD_PATHS : [];
}

export function validateReportTypeFieldDomains(schema: ReportDesignerSchema, issues: ReportDesignerSchemaIssue[]) {
  schema.sections.forEach((section, sectionIndex) => {
    section.blocks.forEach((block, blockIndex) => {
      const blockPath = `$.sections[${sectionIndex}].blocks[${blockIndex}]`;
      const placementIssue = getReportDesignerBlockPlacementIssue(block, section);
      if (placementIssue) {
        issues.push(createIssue("error", blockPath, placementIssue));
      }

      switch (block.type) {
        case "Field":
          validateReportTypeFieldPath(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          break;
        case "Row":
          block.columns.forEach((column, columnIndex) => {
            if (column.contentKind === "Field") {
              validateReportTypeFieldPath(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            }
          });
          break;
        case "Grid":
          block.rows.forEach((row, rowIndex) => {
            row.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup") {
                validateReportTypeFieldPath(schema.reportType, cell.fieldPath, `${blockPath}.rows[${rowIndex}].cells[${cellIndex}].fieldPath`, issues);
              }
            });
          });
          break;
        case "Conditional":
          validateReportTypeFieldPath(schema.reportType, block.condition.fieldPath, `${blockPath}.condition.fieldPath`, issues);
          if (block.content.kind === "Field") {
            validateReportTypeFieldPath(schema.reportType, block.content.fieldPath, `${blockPath}.content.fieldPath`, issues);
          }
          break;
        case "Image":
          if (block.sourceKind === "Field") {
            validateControlledReportImageFieldPath(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          }
          break;
        case "DetailTable":
          if (schema.reportType === "PaymentVoucher") {
            issues.push(createIssue("error", blockPath, "付款/报销模板不能使用出口单据明细表；请用多列行组合付款或费用表格。"));
          }
          if (block.grouping) {
            validateReportTypeFieldPath(schema.reportType, block.grouping.fieldPath, `${blockPath}.grouping.fieldPath`, issues);
            block.grouping.footer?.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Sum") {
                validateReportTypeFieldPath(schema.reportType, cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
                validateDetailTableItemFieldPath(cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
              }
            });
          }
          block.columns.forEach((column, columnIndex) => {
            validateReportTypeFieldPath(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            column.content?.forEach((part, partIndex) => {
              if (part.kind === "Field") {
                validateReportTypeFieldPath(schema.reportType, part.fieldPath, `${blockPath}.columns[${columnIndex}].content[${partIndex}].fieldPath`, issues);
              }
            });
          });
          block.summaryRow?.cells.forEach((cell, cellIndex) => {
            if (cell.contentKind === "Field") {
              validateReportTypeFieldPath(schema.reportType, cell.fieldPath, `${blockPath}.summaryRow.cells[${cellIndex}].fieldPath`, issues);
            }
          });
          if (block.sideBand?.contentKind === "Field") {
            validateReportTypeFieldPath(schema.reportType, block.sideBand.fieldPath, `${blockPath}.sideBand.fieldPath`, issues);
          }
          break;
        case "Text":
        case "PageBreak":
          break;
      }
    });
  });
}

/** Validate an image field against the server-materialized image contract. */
export function validateControlledReportImageFieldPath(
  reportType: ReportDesignerReportType,
  fieldPath: string,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (!fieldPath) return;
  if (isControlledReportImageFieldPath(fieldPath) && getControlledReportImageFieldPaths(reportType).includes(fieldPath)) {
    return;
  }

  issues.push(createIssue(
    "error",
    path,
    "图片字段必须来自受控 data URI 字段（doc_seal_path、customs_seal_path 或 shipping_marks_image_data），不能绑定普通文本或外部 URL。",
  ));
}

/**
 * Validate one field reference against the report's data domain.
 *
 * V2 structured blocks and V3 standalone Field/Image elements must use this
 * same rule.  Keeping the function public avoids a second, weaker V3-only
 * allow-list that could drift from the tested block AST contract.
 */
export function validateReportTypeFieldPath(
  reportType: ReportDesignerReportType,
  fieldPath: string,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (!fieldPath) {
    return;
  }

  if (isTemplateSystemFieldForReportType(reportType, fieldPath)) {
    return;
  }

  if (reportType === "PaymentVoucher") {
    if (fieldPath === "cny_amount_upper" || fieldPath.startsWith("Payment.")) {
      return;
    }

    issues.push(createIssue("error", path, "付款/报销模板只能使用 Payment.*、金额换算或模板系统字段，不能混用出口单据字段。"));
    return;
  }

  if (
    fieldPath.startsWith("Invoice.") ||
    fieldPath.startsWith("Customer.") ||
    fieldPath.startsWith("Exporter.") ||
    fieldPath.startsWith("item.")
  ) {
    return;
  }

  issues.push(createIssue("error", path, "出口单据模板只能使用 Invoice/Customer/Exporter/item 或模板系统字段。"));
}

function validateDetailTableItemFieldPath(
  fieldPath: string,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (!fieldPath) {
    return;
  }

  if (fieldPath.startsWith("Invoice.Items.") || fieldPath.startsWith("item.")) {
    return;
  }

  issues.push(createIssue("error", path, "分组小计求和字段必须来自商品明细 item.* 或 Invoice.Items.*，不能使用发票表头字段。"));
}

function isTemplateSystemFieldForReportType(reportType: ReportDesignerReportType, fieldPath: string) {
  return reportType === "ExportDocument" && (
    fieldPath === "ShowSeal" ||
    fieldPath === "doc_seal_path" ||
    fieldPath === "customs_seal_path" ||
    fieldPath === "shipping_marks_image_data"
  );
}
