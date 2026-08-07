import type {
  ReportDesignerReportType,
  ReportDesignerSchema,
} from "./reportDesignerSchema.ts";
import { getReportDesignerBlockPlacementIssue } from "./reportDesignerModel.ts";
import {
  createIssue,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

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
          validateFieldPathForReportType(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          break;
        case "Row":
          block.columns.forEach((column, columnIndex) => {
            if (column.contentKind === "Field") {
              validateFieldPathForReportType(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            }
          });
          break;
        case "Grid":
          block.rows.forEach((row, rowIndex) => {
            row.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup") {
                validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.rows[${rowIndex}].cells[${cellIndex}].fieldPath`, issues);
              }
            });
          });
          break;
        case "Conditional":
          validateFieldPathForReportType(schema.reportType, block.condition.fieldPath, `${blockPath}.condition.fieldPath`, issues);
          if (block.content.kind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.content.fieldPath, `${blockPath}.content.fieldPath`, issues);
          }
          break;
        case "Image":
          if (block.sourceKind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          }
          break;
        case "DetailTable":
          if (schema.reportType === "PaymentVoucher") {
            issues.push(createIssue("error", blockPath, "付款/报销模板不能使用出口单据明细表；请用多列行组合付款或费用表格。"));
          }
          if (block.grouping) {
            validateFieldPathForReportType(schema.reportType, block.grouping.fieldPath, `${blockPath}.grouping.fieldPath`, issues);
            block.grouping.footer?.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Sum") {
                validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
                validateDetailTableItemFieldPath(cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
              }
            });
          }
          block.columns.forEach((column, columnIndex) => {
            validateFieldPathForReportType(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            column.content?.forEach((part, partIndex) => {
              if (part.kind === "Field") {
                validateFieldPathForReportType(schema.reportType, part.fieldPath, `${blockPath}.columns[${columnIndex}].content[${partIndex}].fieldPath`, issues);
              }
            });
          });
          block.summaryRow?.cells.forEach((cell, cellIndex) => {
            if (cell.contentKind === "Field") {
              validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.summaryRow.cells[${cellIndex}].fieldPath`, issues);
            }
          });
          if (block.sideBand?.contentKind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.sideBand.fieldPath, `${blockPath}.sideBand.fieldPath`, issues);
          }
          break;
        case "Text":
        case "PageBreak":
          break;
      }
    });
  });
}

function validateFieldPathForReportType(
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
