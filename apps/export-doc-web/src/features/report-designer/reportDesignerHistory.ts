import { useEffect, useReducer } from "react";
import { findFirstBlock } from "./reportDesignerSelection.ts";
import type { ReportDesignerSchema } from "./reportDesignerSchema.ts";

export type ReportDesignerDocumentState = {
  schema: ReportDesignerSchema;
  selectedBlockId: string | null;
  selectedSectionId: string | null;
};

type ReportDesignerHistoryState = {
  past: ReportDesignerDocumentState[];
  present: ReportDesignerDocumentState;
  future: ReportDesignerDocumentState[];
};

type ReportDesignerHistoryAction =
  | { type: "replace"; state: ReportDesignerDocumentState }
  | { type: "select"; blockId: string | null }
  | { type: "selectSection"; sectionId: string | null }
  | { type: "commit"; state: ReportDesignerDocumentState }
  | { type: "undo" }
  | { type: "redo" };

export function useReportDesignerHistory(initialSchema: ReportDesignerSchema) {
  const [history, dispatch] = useReducer(reportDesignerHistoryReducer, initialSchema, createInitialHistory);

  useEffect(() => {
    dispatch({ type: "replace", state: createInitialDocumentState(initialSchema) });
  }, [initialSchema]);

  return {
    state: history.present,
    canUndo: history.past.length > 0,
    canRedo: history.future.length > 0,
    selectBlock: (blockId: string | null) => dispatch({ type: "select", blockId }),
    selectSection: (sectionId: string | null) => dispatch({ type: "selectSection", sectionId }),
    commitState: (state: ReportDesignerDocumentState) => dispatch({ type: "commit", state }),
    undo: () => dispatch({ type: "undo" }),
    redo: () => dispatch({ type: "redo" }),
  };
}

function createInitialHistory(schema: ReportDesignerSchema): ReportDesignerHistoryState {
  return {
    past: [],
    present: createInitialDocumentState(schema),
    future: [],
  };
}

function createInitialDocumentState(schema: ReportDesignerSchema): ReportDesignerDocumentState {
  return {
    schema,
    selectedBlockId: findFirstBlock(schema)?.id ?? null,
    selectedSectionId: null,
  };
}

function reportDesignerHistoryReducer(
  history: ReportDesignerHistoryState,
  action: ReportDesignerHistoryAction,
): ReportDesignerHistoryState {
  switch (action.type) {
    case "replace":
      return {
        past: [],
        present: action.state,
        future: [],
      };
    case "select":
      return {
        ...history,
        present: {
          ...history.present,
          selectedBlockId: action.blockId,
          selectedSectionId: null,
        },
      };
    case "selectSection":
      return {
        ...history,
        present: {
          ...history.present,
          selectedBlockId: null,
          selectedSectionId: action.sectionId,
        },
      };
    case "commit":
      return {
        past: [...history.past, history.present].slice(-50),
        present: action.state,
        future: [],
      };
    case "undo": {
      const previous = history.past[history.past.length - 1];
      if (!previous) {
        return history;
      }

      return {
        past: history.past.slice(0, -1),
        present: previous,
        future: [history.present, ...history.future],
      };
    }
    case "redo": {
      const next = history.future[0];
      if (!next) {
        return history;
      }

      return {
        past: [...history.past, history.present],
        present: next,
        future: history.future.slice(1),
      };
    }
  }
}
