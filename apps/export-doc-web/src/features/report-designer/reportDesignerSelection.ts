import type { ReportBlock, ReportDesignerSchema, ReportSection } from "./reportDesignerSchema.ts";

export type ReportDesignerSelection = {
  selectedBlockId: string | null;
  selectedSectionId: string | null;
};

export function findSelectedBlock(schema: ReportDesignerSchema, selectedBlockId: string | null) {
  if (!selectedBlockId) {
    return null;
  }

  for (const section of schema.sections) {
    const block = section.blocks.find((candidate) => candidate.id === selectedBlockId);
    if (block) {
      return { section, block };
    }
  }

  return null;
}

export function findSelectedSection(schema: ReportDesignerSchema, selectedSectionId: string | null) {
  if (!selectedSectionId) {
    return null;
  }

  return schema.sections.find((section) => section.id === selectedSectionId) ?? null;
}

export function findFirstBlock(schema: ReportDesignerSchema): ReportBlock | null {
  return schema.sections.flatMap((section) => section.blocks)[0] ?? null;
}

export function sectionLabel(section: ReportSection) {
  switch (section.type) {
    case "Header":
      return "页眉";
    case "Body":
      return "主体";
    case "Footer":
      return "页脚";
  }
}

export function blockLabel(block: ReportBlock) {
  switch (block.type) {
    case "Text":
      return "文本";
    case "Field":
      return "字段";
    case "Row":
      return "行/多列";
    case "Grid":
      return "票据表格";
    case "Conditional":
      return "条件块";
    case "Image":
      return "图片/印章";
    case "DetailTable":
      return "明细表";
    case "PageBreak":
      return "分页符";
  }
}
