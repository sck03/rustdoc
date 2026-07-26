import { useEffect, useRef, type RefObject } from "react";

type ModalDialogOptions = {
  active?: boolean;
  canClose?: boolean;
  initialFocusRef?: RefObject<HTMLElement | null>;
};

/**
 * Keeps keyboard focus inside a dialog and restores focus to the invoking
 * control when the dialog closes.  Business dialogs should use this hook
 * instead of implementing slightly different Escape/Tab behaviour locally.
 */
export function useModalDialog<T extends HTMLElement = HTMLDivElement>(
  onClose: () => void,
  options: ModalDialogOptions = {},
) {
  const { active = true, canClose = true, initialFocusRef } = options;
  const dialogRef = useRef<T | null>(null);
  const onCloseRef = useRef(onClose);
  const canCloseRef = useRef(canClose);
  const initialFocusRefRef = useRef(initialFocusRef);

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    canCloseRef.current = canClose;
    initialFocusRefRef.current = initialFocusRef;
  }, [canClose, initialFocusRef]);

  useEffect(() => {
    if (!active) {
      return undefined;
    }

    const previouslyFocusedElement = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const focusInitialElement = window.requestAnimationFrame(() => {
      const preferred = initialFocusRefRef.current?.current;
      if (preferred && !preferred.hasAttribute("disabled")) {
        preferred.focus();
        return;
      }

      dialogRef.current?.querySelector<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
      )?.focus();
    });

    function getFocusableElements() {
      return Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );
    }

    function handleKeyDown(event: KeyboardEvent) {
      const activeElement = document.activeElement;
      // A confirmation dialog may be opened from inside this dialog.  Only
      // the topmost dialog that owns focus should consume Escape/Tab; this
      // keeps a parent dialog from closing or cycling focus underneath it.
      if (!dialogRef.current || (activeElement && !dialogRef.current.contains(activeElement))) {
        return;
      }

      if (event.key === "Escape") {
        if (!canCloseRef.current) {
          return;
        }
        event.preventDefault();
        onCloseRef.current();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusable = getFocusableElements();
      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.cancelAnimationFrame(focusInitialElement);
      window.removeEventListener("keydown", handleKeyDown);
      previouslyFocusedElement?.focus();
    };
  }, [active]);

  return dialogRef;
}
