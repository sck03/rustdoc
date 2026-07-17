import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDesignerDocumentState } from "./reportDesignerHistory.ts";
import { normalizeRowColumnWidths } from "./reportDesignerMutations.ts";
import type {
  ReportBlock,
  ReportBorderStyle,
  ReportConditionalContent,
  ReportConditionalRule,
  ReportDesignerSchema,
  ReportDetailTableCellContent,
  ReportDetailTableGroupFooterCell,
  ReportDetailTableSummaryCell,
  ReportGridCell,
  ReportSection,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";

export function normalizeBorderForEditor(border?: ReportBorderStyle): ReportBorderStyle {
  return {
    color: border?.color ?? "#333333",
    widthPx: border?.widthPx ?? 0,
    style: border?.style ?? "Solid",
    top: Boolean(border?.top),
    right: Boolean(border?.right),
    bottom: Boolean(border?.bottom),
    left: Boolean(border?.left),
  };
}

export function readDefaultRowFieldPath(fieldGroups: ReportDesignerFieldGroup[]) {
  const field = fieldGroups
    .flatMap((group) => group.fields)
    .find((candidate) =>
      candidate.value !== "ShowSeal" &&
      candidate.value !== "doc_seal_path" &&
      candidate.value !== "customs_seal_path" &&
      candidate.value !== "shipping_marks_image_data" &&
      !candidate.value.startsWith("item."),
    );

  return field?.value ?? "";
}

export function filterDetailItemFieldGroups(fieldGroups: ReportDesignerFieldGroup[]) {
  return fieldGroups
    .map((group) => ({
      ...group,
      fields: group.fields.filter((field) => field.value.startsWith("item.") || field.value.startsWith("Invoice.Items.")),
    }))
    .filter((group) => group.fields.length > 0);
}

export function updatePage(
  documentState: ReportDesignerDocumentState,
  pagePatch: Partial<ReportDesignerSchema["page"]>,
): ReportDesignerDocumentState {
  return {
    ...documentState,
    schema: {
      ...documentState.schema,
      page: {
        ...documentState.schema.page,
        ...pagePatch,
      },
    },
  };
}

export function updateSectionPrint(
  documentState: ReportDesignerDocumentState,
  sectionId: string,
  patch: Partial<ReportSection["print"]>,
): ReportDesignerDocumentState {
  return {
    ...documentState,
    schema: {
      ...documentState.schema,
      sections: documentState.schema.sections.map((section) =>
        section.id === sectionId
          ? {
              ...section,
              print: {
                ...section.print,
                ...patch,
                repeatOnEveryPage: section.type === "Body" ? false : (patch.repeatOnEveryPage ?? section.print.repeatOnEveryPage),
                pinToPageBottom: section.type === "Footer" ? (patch.pinToPageBottom ?? section.print.pinToPageBottom) : false,
              },
            }
          : section,
      ),
    },
    selectedBlockId: null,
    selectedSectionId: sectionId,
  };
}

export function normalizePageSize(value: string): ReportDesignerSchema["page"]["size"] {
  if (value === "A5" || value === "Letter" || value === "Custom") {
    return value;
  }

  return "A4";
}

export function normalizeNumber(value: string, fallback: number) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
}

export function roundDesignerWidth(value: number) {
  return Math.round(value * 10) / 10;
}

export function formatDesignerWidth(value: number) {
  return String(roundDesignerWidth(value));
}

export function normalizeAlign(value: string): NonNullable<ReportTextStyle["align"]> {
  if (value === "Center" || value === "Right") {
    return value;
  }

  return "Left";
}

export function normalizeSummaryContentKind(value: string): ReportDetailTableSummaryCell["contentKind"] {
  if (value === "Field" || value === "Text") {
    return value;
  }

  return "Empty";
}

export function normalizeGroupFooterContentKind(value: string): ReportDetailTableGroupFooterCell["contentKind"] {
  if (value === "Sum" || value === "Count" || value === "Text") {
    return value;
  }

  return "Empty";
}

export function normalizeDetailCellPartKind(value: string): ReportDetailTableCellContent["kind"] {
  if (value === "Field" || value === "LineBreak") {
    return value;
  }

  return "Text";
}

export function normalizeGridCellContentKind(value: string): ReportGridCell["contentKind"] {
  if (value === "Field" || value === "CheckboxGroup") {
    return value;
  }

  return "Text";
}

export function normalizeBorderLineStyle(value: string): NonNullable<ReportBorderStyle["style"]> {
  if (value === "Dashed" || value === "None") {
    return value;
  }

  return "Solid";
}

export function normalizeConditionalOperator(value: string): ReportConditionalRule["operator"] {
  if (value === "Equals" || value === "NotEquals") {
    return value;
  }

  return "HasValue";
}

export function normalizeConditionalContentKind(value: string): ReportConditionalContent["kind"] {
  if (value === "Text") {
    return "Text";
  }

  return "Field";
}

export function normalizeImageSourceKind(value: string): Extract<ReportBlock, { type: "Image" }>["sourceKind"] {
  if (value === "StaticUrl") {
    return "StaticUrl";
  }

  return "Field";
}

export function createEmptySummaryCell(columnId: string): ReportDetailTableSummaryCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

export function createEmptyGroupFooterCell(columnId: string): ReportDetailTableGroupFooterCell {
  return {
    columnId,
    contentKind: "Empty",
    text: "",
    fieldPath: "",
  };
}

