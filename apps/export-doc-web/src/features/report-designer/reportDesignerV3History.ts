import { useEffect, useMemo, useState } from "react";
import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";
import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { createReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";

type HistoryState = {
  past: ReportDesignerV3DocumentState[];
  present: ReportDesignerV3DocumentState;
  future: ReportDesignerV3DocumentState[];
  coalescedAt: number;
};

type CommitOptions = {
  coalesce?: boolean;
};

// Large templates retain structural sharing between snapshots, but a bounded
// count alone can still reserve too much memory after repeated bulk edits.
export const REPORT_DESIGNER_V3_HISTORY_MAX_ENTRIES = 40;
export const REPORT_DESIGNER_V3_HISTORY_MAX_ESTIMATED_BYTES = 12 * 1024 * 1024;
const stateSizeCache = new WeakMap<object, number>();

export function useReportDesignerV3History(initialSchema: ReportDesignerV3Schema) {
  const initialState = useMemo(() => createReportDesignerV3DocumentState(initialSchema), [initialSchema]);
  const [history, setHistory] = useState<HistoryState>(() => ({ past: [], present: initialState, future: [], coalescedAt: 0 }));

  useEffect(() => {
    setHistory({ past: [], present: initialState, future: [], coalescedAt: 0 });
  }, [initialState]);

  return {
    state: history.present,
    canUndo: history.past.length > 0,
    canRedo: history.future.length > 0,
    commit(next: ReportDesignerV3DocumentState, options: CommitOptions = {}) {
      const coalescedAt = options.coalesce ? performance.now() : 0;
      setHistory((current) => {
        if (current.present.schema === next.schema) return { ...current, present: next };
        // Keep grouping inside the state transition so React can replay an
        // updater without consuming the undo boundary of the first execution.
        const coalesce = coalescedAt > 0 && current.coalescedAt > 0 && coalescedAt - current.coalescedAt <= 450 && current.past.length > 0;
        return coalesce
          ? { ...current, present: next, future: [], coalescedAt }
          : trimHistory({ past: [...current.past, current.present], present: next, future: [], coalescedAt });
      });
    },
    commitFrom(base: ReportDesignerV3DocumentState, next: ReportDesignerV3DocumentState) {
      if (next.schema === base.schema) return;
      setHistory((current) => {
        // A delayed pointer-up must never append a snapshot based on a stale
        // render.  The current present is the only authoritative base; if it
        // changed during the gesture, discard that terminal commit safely.
        if (current.present.schema !== base.schema) return current;
        return trimHistory({ past: [...current.past, base], present: next, future: [], coalescedAt: 0 });
      });
    },
    reset(schema: ReportDesignerV3Schema) {
      setHistory({ past: [], present: createReportDesignerV3DocumentState(schema), future: [], coalescedAt: 0 });
    },
    select(selectedIds: string[], activeLayerId?: string | null) {
      setHistory((current) => {
        const layerId = activeLayerId === undefined ? current.present.activeLayerId : activeLayerId;
        const unchanged = layerId === current.present.activeLayerId && selectedIds.length === current.present.selectedIds.length && selectedIds.every((id, index) => id === current.present.selectedIds[index]);
        if (unchanged && !current.coalescedAt) return current;
        return { ...current, coalescedAt: 0, present: unchanged ? current.present : { ...current.present, selectedIds, activeLayerId: layerId } };
      });
    },
    undo() {
      setHistory((current) => {
        const previous = current.past.at(-1);
        return previous
          ? trimHistory({ past: current.past.slice(0, -1), present: previous, future: [current.present, ...current.future], coalescedAt: 0 })
          : current;
      });
    },
    redo() {
      setHistory((current) => {
        const next = current.future[0];
        return next
          ? trimHistory({ past: [...current.past, current.present], present: next, future: current.future.slice(1), coalescedAt: 0 })
          : current;
      });
    },
  };
}

function trimHistory(history: HistoryState): HistoryState {
  let past = history.past;
  let future = history.future;
  let estimatedBytes = estimateHistoryBytes(past, future);

  while (past.length + future.length > REPORT_DESIGNER_V3_HISTORY_MAX_ENTRIES ||
         estimatedBytes > REPORT_DESIGNER_V3_HISTORY_MAX_ESTIMATED_BYTES) {
    // Keep the most recent undo states. Once that side is exhausted, discard
    // the oldest redo state instead of retaining an unbounded stale branch.
    if (past.length > 0) {
      const removed = past[0];
      past = past.slice(1);
      estimatedBytes -= estimateStateBytes(removed);
    } else if (future.length > 0) {
      const removed = future.at(-1);
      future = future.slice(0, -1);
      estimatedBytes -= removed ? estimateStateBytes(removed) : 0;
    } else {
      break;
    }
  }

  return past === history.past && future === history.future ? history : { ...history, past, future };
}

function estimateHistoryBytes(
  past: ReportDesignerV3DocumentState[],
  future: ReportDesignerV3DocumentState[],
) {
  return [...past, ...future].reduce((total, state) => total + estimateStateBytes(state), 0);
}

function estimateStateBytes(state: ReportDesignerV3DocumentState) {
  // This guard runs only at commit/undo boundaries, never while the pointer
  // moves, so an accurate-enough estimate cannot slow canvas dragging.
  const cached = stateSizeCache.get(state.schema);
  if (cached !== undefined) return cached;
  try {
    const bytes = JSON.stringify(state.schema).length * 2;
    stateSizeCache.set(state.schema, bytes);
    return bytes;
  } catch {
    return REPORT_DESIGNER_V3_HISTORY_MAX_ESTIMATED_BYTES;
  }
}
