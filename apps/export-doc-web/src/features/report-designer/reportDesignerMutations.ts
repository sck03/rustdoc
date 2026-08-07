import type { ReportDesignerDocumentState } from "./reportDesignerHistory.ts";
import type {
  ReportBlock,
  ReportDesignerSchema,
  ReportSection,
} from "./reportDesignerSchema.ts";
import {
  findFirstSectionAllowingBlock,
  isReportDesignerBlockAllowedInSection,
} from "./reportDesignerModel.ts";
import { findSelectedBlock, findSelectedSection } from "./reportDesignerSelection.ts";
import { createPageBreakBlock } from "./reportDesignerBlockFactories.ts";
import { createReportBlockId } from "./reportDesignerMutationUtils.ts";

export type ReportDesignerBlockDropTarget = {
  sectionId: string;
  blockId?: string;
  placement: "before" | "after" | "inside";
};

export * from "./reportDesignerBlockFactories.ts";
export * from "./reportDesignerTableMutations.ts";
export { normalizeDesignerFieldPath } from "./reportDesignerMutationUtils.ts";

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
