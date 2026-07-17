import type { ReportBlock, ReportDesignerSchema, ReportSection } from "./reportDesignerSchema.ts";

export type ReportDesignerModelBinding = {
  label: string;
  fieldPath: string;
};

export type ReportDesignerSchemaModelSummary = {
  reportTypeLabel: string;
  pageLabel: string;
  sectionCount: number;
  blockCount: number;
  disabledBlockCount: number;
  fieldBindingCount: number;
  dataSources: string[];
};

export function reportSectionModelRole(section: ReportSection) {
  switch (section.type) {
    case "Header":
      return "页眉 band";
    case "Body":
      return "主体 / 明细 band";
    case "Footer":
      return "页脚 band";
  }
}

export function reportBlockModelRole(block: ReportBlock) {
  switch (block.type) {
    case "Text":
      return "静态文本";
    case "Field":
      return "数据字段";
    case "Row":
      return "多列布局";
    case "Grid":
      return "固定票据格";
    case "Conditional":
      return "条件 band";
    case "Image":
      return "图片/印章";
    case "DetailTable":
      return "明细数据 band";
    case "PageBreak":
      return "分页控制";
  }
}

export function isReportDesignerBlockAllowedInSection(block: ReportBlock, section: ReportSection) {
  switch (block.type) {
    case "DetailTable":
    case "PageBreak":
      return section.type === "Body";
    case "Text":
    case "Field":
    case "Row":
    case "Grid":
    case "Conditional":
    case "Image":
      return true;
  }
}

export function getReportDesignerBlockPlacementIssue(block: ReportBlock, section: ReportSection) {
  if (isReportDesignerBlockAllowedInSection(block, section)) {
    return "";
  }

  switch (block.type) {
    case "DetailTable":
      return "明细表是主体明细 band，只能放在主体版区。";
    case "PageBreak":
      return "分页符只能放在主体版区，不能放入重复页眉或页脚。";
    default:
      return "该组件不能放在当前版区。";
  }
}

export function findFirstSectionAllowingBlock(schema: ReportDesignerSchema, block: ReportBlock) {
  return schema.sections.find((section) => isReportDesignerBlockAllowedInSection(block, section)) ?? null;
}

export function summarizeReportDesignerSchemaModel(schema: ReportDesignerSchema): ReportDesignerSchemaModelSummary {
  const blocks = schema.sections.flatMap((section) => section.blocks);
  const fieldBindings = blocks.flatMap(collectReportDesignerBlockFieldBindings);

  return {
    reportTypeLabel: schema.reportType === "PaymentVoucher" ? "付款/报销" : "出口单据",
    pageLabel: renderPageModelLabel(schema),
    sectionCount: schema.sections.length,
    blockCount: blocks.length,
    disabledBlockCount: blocks.filter((block) => block.output?.enabled === false).length,
    fieldBindingCount: fieldBindings.length,
    dataSources: collectReportDesignerDataSources(schema, fieldBindings),
  };
}

export function collectReportDesignerBlockFieldBindings(block: ReportBlock): ReportDesignerModelBinding[] {
  switch (block.type) {
    case "Text":
    case "PageBreak":
      return [];
    case "Field":
      return compactModelBindings([{ label: block.label || "字段", fieldPath: block.fieldPath }]);
    case "Row":
      return compactModelBindings(block.columns
        .filter((column) => column.contentKind === "Field")
        .map((column, index) => ({
          label: column.label || `多列行第 ${index + 1} 列`,
          fieldPath: column.fieldPath,
        })));
    case "Grid":
      return compactModelBindings(block.rows.flatMap((row, rowIndex) =>
        row.cells
          .filter((cell) => cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup")
          .map((cell, cellIndex) => ({
            label: cell.label || `票据格 ${rowIndex + 1}-${cellIndex + 1}`,
            fieldPath: cell.fieldPath,
          })),
      ));
    case "Conditional":
      return compactModelBindings([
        { label: "条件字段", fieldPath: block.condition.fieldPath },
        block.content.kind === "Field"
          ? { label: block.content.label || "输出字段", fieldPath: block.content.fieldPath }
          : null,
      ]);
    case "Image":
      return block.sourceKind === "Field"
        ? compactModelBindings([{ label: block.title || "图片来源", fieldPath: block.fieldPath }])
        : [];
    case "DetailTable":
      return compactModelBindings([
        { label: "明细数据源", fieldPath: block.sourcePath },
        block.sideBand?.contentKind === "Field"
          ? { label: block.sideBand.title || "非循环侧栏", fieldPath: block.sideBand.fieldPath }
          : null,
        block.grouping ? { label: block.grouping.label || "分组字段", fieldPath: block.grouping.fieldPath } : null,
        ...block.columns.flatMap((column, index) => {
          if (column.contentKind === "Composite" && column.content?.length) {
            return column.content
              .filter((part) => part.kind === "Field")
              .map((part) => ({
                label: `${column.title || `明细列 ${index + 1}`} 片段`,
                fieldPath: part.fieldPath,
              }));
          }

          return [{ label: column.title || `明细列 ${index + 1}`, fieldPath: column.fieldPath }];
        }),
        ...(block.grouping?.footer?.cells ?? [])
          .filter((cell) => cell.contentKind === "Sum")
          .map((cell) => ({ label: "分组小计", fieldPath: cell.fieldPath })),
        ...(block.summaryRow?.cells ?? [])
          .filter((cell) => cell.contentKind === "Field")
          .map((cell) => ({ label: "表尾合计", fieldPath: cell.fieldPath })),
      ]);
  }
}

function collectReportDesignerDataSources(
  schema: ReportDesignerSchema,
  bindings: ReportDesignerModelBinding[],
) {
  const sources = new Set<string>();
  if (schema.reportType === "PaymentVoucher") {
    sources.add("Payment");
  } else {
    sources.add("Invoice");
  }

  bindings.forEach((binding) => {
    if (isTemplateSystemModelField(binding.fieldPath)) {
      return;
    }

    if (binding.fieldPath === "Invoice.Items" || binding.fieldPath.startsWith("Invoice.Items.") || binding.fieldPath.startsWith("item.")) {
      sources.add("Invoice.Items");
      return;
    }

    const root = binding.fieldPath.split(".")[0];
    if (root) {
      sources.add(root);
    }
  });

  return Array.from(sources).sort((left, right) => {
    const order = ["Invoice", "Invoice.Items", "Customer", "Exporter", "Payment"];
    const leftIndex = order.indexOf(left);
    const rightIndex = order.indexOf(right);
    if (leftIndex >= 0 || rightIndex >= 0) {
      return (leftIndex >= 0 ? leftIndex : order.length) - (rightIndex >= 0 ? rightIndex : order.length);
    }

    return left.localeCompare(right);
  });
}

function isTemplateSystemModelField(fieldPath: string) {
  return fieldPath === "ShowSeal" ||
    fieldPath === "doc_seal_path" ||
    fieldPath === "customs_seal_path" ||
    fieldPath === "shipping_marks_image_data";
}

function renderPageModelLabel(schema: ReportDesignerSchema) {
  const size = schema.page.size === "Custom"
    ? `${schema.page.widthMm ?? 210}x${schema.page.heightMm ?? 297}mm`
    : schema.page.size;
  const orientation = schema.page.orientation === "Landscape" ? "横版" : "竖版";
  return `${size} ${orientation}`;
}

function compactModelBindings(values: Array<ReportDesignerModelBinding | null>) {
  const seen = new Set<string>();
  return values.filter((value): value is ReportDesignerModelBinding => {
    const fieldPath = value?.fieldPath.trim();
    if (!value || !fieldPath) {
      return false;
    }

    const key = `${value.label}:${fieldPath}`;
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}
