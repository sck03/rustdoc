import type {
  ReportDesignerReportType,
  ReportDesignerSchema,
} from "./reportDesignerSchema.ts";
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
      if (section.type !== "Body" && (block.type === "DetailTable" || block.type === "PageBreak")) {
        issues.push(createIssue("error", blockPath, block.type === "DetailTable"
          ? "明细表只能放在主体版区。"
          : "分页符只能放在主体版区，不能放入重复页眉或页脚。"));
      }

      switch (block.type) {
        case "Field":
          validateReportTypeFieldPath(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          break;
        case "Row":
          block.columns.forEach((column, columnIndex) => {
            if (column.contentKind === "Field") {
              validateFixedReportFieldPath(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            }
          });
          break;
        case "Grid":
          block.rows.forEach((row, rowIndex) => {
            row.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup") {
                validateFixedReportFieldPath(schema.reportType, cell.fieldPath, `${blockPath}.rows[${rowIndex}].cells[${cellIndex}].fieldPath`, issues);
              }
            });
          });
          break;
        case "Conditional":
          validateFixedReportFieldPath(schema.reportType, block.condition.fieldPath, `${blockPath}.condition.fieldPath`, issues);
          block.additionalConditions?.forEach((rule, index) => validateFixedReportFieldPath(schema.reportType, rule.fieldPath, `${blockPath}.additionalConditions[${index}].fieldPath`, issues));
          if (block.content.kind === "Field") {
            validateFixedReportFieldPath(schema.reportType, block.content.fieldPath, `${blockPath}.content.fieldPath`, issues);
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
            validateDetailTableItemFieldPath(column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            column.content?.forEach((part, partIndex) => {
              if (part.condition) {
                validateReportTypeFieldPath(schema.reportType, part.condition.fieldPath, `${blockPath}.columns[${columnIndex}].content[${partIndex}].condition.fieldPath`, issues);
                if (isControlledReportImageFieldPath(part.condition.fieldPath)) issues.push(createIssue("error", blockPath, "条件字段请使用普通文字或数字字段。"));
              }
              if (part.kind === "Field") {
                validateReportTypeFieldPath(schema.reportType, part.fieldPath, `${blockPath}.columns[${columnIndex}].content[${partIndex}].fieldPath`, issues);
              }
            });
          });
          (["summaryRow", "introRow"] as const).forEach(key => block[key]?.cells.forEach((cell, cellIndex) => {
            if (cell.contentKind === "Field") {
              validateFixedReportFieldPath(schema.reportType, cell.fieldPath, `${blockPath}.${key}.cells[${cellIndex}].fieldPath`, issues);
            }
          }));
          if (block.sideBand?.contentKind === "Field") {
            validateFixedReportFieldPath(schema.reportType, block.sideBand.fieldPath, `${blockPath}.sideBand.fieldPath`, issues);
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
    "图片字段只能使用受控印章；唛头请使用“唛头”字段，自动显示文字或图片。",
  ));
}

/**
 * Validate one field reference against the report's data domain.
 *
 * Structured blocks and standalone Field/Image elements must use this
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

  if (reportType === "OaApproval") {
    if (!fieldPath.startsWith("Oa.")) issues.push(createIssue("error", path, "审批单只能使用 OA 审批字段。"));
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

  if (/^total_by_(ctn|qty)_unit(?:\.(Value|Key))?$/.test(fieldPath) || fieldPath === "total_cartons_and_quantity") return;
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

  issues.push(createIssue("error", path, "明细列和分组小计求和字段必须来自商品明细 item.* 或 Invoice.Items.*，不能使用发票表头字段。"));
}

export function validateFixedReportFieldPath(reportType: ReportDesignerReportType, fieldPath: string, path: string, issues: ReportDesignerSchemaIssue[]) {
  validateReportTypeFieldPath(reportType, fieldPath, path, issues);
  if (fieldPath.startsWith("item.")) issues.push(createIssue("error", path, "不能在商品循环外使用 item 字段；请使用独立商品字段或明细表正文。"));
}

function isTemplateSystemFieldForReportType(reportType: ReportDesignerReportType, fieldPath: string) {
  return reportType === "ExportDocument" && (
    fieldPath === "ShowSeal" ||
    fieldPath === "doc_seal_path" ||
    fieldPath === "customs_seal_path"
  );
}
