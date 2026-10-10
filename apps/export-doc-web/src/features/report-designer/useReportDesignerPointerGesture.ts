import { useCallback, useEffect, useMemo, useRef } from "react";

type PointerPoint = { x: number; y: number };
type PointerStart = Pick<PointerEvent, "button" | "pointerId" | "clientX" | "clientY"> & { currentTarget: HTMLElement };
type GestureCallbacks = {
  surface: Element;
  preview: (point: PointerPoint) => void;
  commit: (point: PointerPoint) => void;
  restore: () => void;
};

/** Own the transient pointer lifecycle; document mutations stay with the caller. */
export function useReportDesignerPointerGesture() {
  const active = useRef<{ cancel: () => void } | null>(null);
  const cancel = useCallback(() => active.current?.cancel(), []);
  const start = useCallback((event: PointerStart, callbacks: GestureCallbacks) => {
    if (event.button !== 0 || active.current) return false;
    const { currentTarget: target, pointerId } = event;
    const origin = callbacks.surface.getBoundingClientRect();
    let point = { x: event.clientX, y: event.clientY };
    let frame: number | null = null;
    const session = { cancel: () => finish(true) };
    active.current = session;

    function sample(event: PointerEvent) {
      point = {
        x: Number.isFinite(event.clientX) ? event.clientX : point.x,
        y: Number.isFinite(event.clientY) ? event.clientY : point.y,
      };
    }
    function move(event: PointerEvent) {
      if (event.pointerId !== pointerId) return;
      sample(event);
      if (frame !== null) return;
      frame = requestAnimationFrame(() => { frame = null; callbacks.preview(point); });
    }
    function release(event: PointerEvent) {
      if (event.pointerId !== pointerId) return;
      sample(event);
      finish(event.type === "pointercancel");
    }
    function keyDown(event: KeyboardEvent) {
      if (event.key !== "Escape" || event.isComposing) return;
      event.preventDefault();
      event.stopPropagation();
      finish(true);
    }
    function scroll() {
      const current = callbacks.surface.getBoundingClientRect();
      if (current.left !== origin.left || current.top !== origin.top) finish(true);
    }
    function finish(cancelled: boolean) {
      if (active.current !== session) return;
      active.current = null;
      if (frame !== null) cancelAnimationFrame(frame);
      window.removeEventListener("pointermove", move, true);
      window.removeEventListener("pointerup", release, true);
      window.removeEventListener("pointercancel", release, true);
      window.removeEventListener("keydown", keyDown, true);
      window.removeEventListener("blur", session.cancel);
      window.removeEventListener("resize", session.cancel);
      window.removeEventListener("scroll", scroll, true);
      try {
        if (!cancelled) callbacks.commit(point);
      } finally {
        try { callbacks.restore(); }
        finally {
          try { if (target.hasPointerCapture(pointerId)) target.releasePointerCapture(pointerId); }
          catch { /* Capture may already have been released by the WebView. */ }
        }
      }
    }

    // One event path also works when a WebView rejects or loses pointer capture.
    window.addEventListener("pointermove", move, true);
    window.addEventListener("pointerup", release, true);
    window.addEventListener("pointercancel", release, true);
    window.addEventListener("keydown", keyDown, true);
    window.addEventListener("blur", session.cancel);
    window.addEventListener("resize", session.cancel);
    window.addEventListener("scroll", scroll, true);
    try { target.setPointerCapture(pointerId); }
    catch { /* Window listeners retain the terminal and cancellation events. */ }
    return true;
  }, []);
  useEffect(() => cancel, [cancel]);
  return useMemo(() => ({ start, cancel, isActive: () => active.current !== null }), [start, cancel]);
}
