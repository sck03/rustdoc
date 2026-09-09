import { useEffect, useEffectEvent, type RefObject } from "react";
import { deleteSelectedV3Elements, moveSelectedV3Elements, selectAllV3Elements, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import type { useReportDesignerV3History } from "./reportDesignerV3History.ts";
import { isEditableTarget } from "./reportDesignerV3WorkspaceHelpers.tsx";

export function useReportDesignerV3Shortcuts({ workspaceRef, history, editable, commit, copySelection, pasteClipboard, duplicateSelection, clearSelection }: {
  workspaceRef: RefObject<HTMLElement | null>; history: ReturnType<typeof useReportDesignerV3History>; editable: boolean;
  commit: (next: ReportDesignerV3DocumentState, options?: { coalesce?: boolean }) => void;
  copySelection: () => void; pasteClipboard: () => void; duplicateSelection: () => void; clearSelection: () => void;
}) {
  const handleKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (event.defaultPrevented || event.altKey || event.isComposing || isEditableTarget(event.target)) return;
    const modifier = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();
    if (key === "escape") { clearSelection(); return; }
    if (!editable) return;
    let command: (() => void) | undefined;
    if (modifier) {
      if (key === "a") command = () => commit(selectAllV3Elements(history.state));
      if (key === "c") command = copySelection;
      if (key === "v") command = pasteClipboard;
      if (key === "d") command = duplicateSelection;
      if (key === "z") command = event.shiftKey ? history.redo : history.undo;
      if (key === "y") command = history.redo;
    } else if (history.state.selectedIds.length) {
      if (key === "delete" || key === "backspace") command = () => commit(deleteSelectedV3Elements(history.state));
      if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(event.key)) {
        const step = event.shiftKey ? 500 : 100;
        const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
        const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
        command = () => commit(moveSelectedV3Elements(history.state, dx, dy, false), { coalesce: true });
      }
    }
    if (command) { event.preventDefault(); command(); }
  });
  useEffect(() => {
    const node = workspaceRef.current;
    node?.addEventListener("keydown", handleKeyDown);
    return () => node?.removeEventListener("keydown", handleKeyDown);
  }, [workspaceRef]);
}
