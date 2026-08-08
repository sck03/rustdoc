import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useLocation } from "react-router-dom";
import { useConfirmation } from "./ConfirmationProvider.tsx";

const defaultUnsavedChangesMessage = "当前页面有未保存的修改。";

type UnsavedChangesEntry = {
  id: string;
  isDirty: boolean;
  message: string;
};

type UnsavedChangesContextValue = {
  confirmDiscardChanges: (actionLabel?: string) => Promise<boolean>;
  confirmEntryDiscardChanges: (entry: UnsavedChangesEntry, actionLabel?: string) => Promise<boolean>;
  hasUnsavedChanges: boolean;
  removeEntry: (id: string) => void;
  setEntry: (entry: UnsavedChangesEntry) => void;
};

type UnsavedChangesGuardOptions = {
  isDirty: boolean;
  message?: string;
};

type PendingHistoryNavigation = {
  originIndex: number;
  targetIndex: number;
  delta: number;
  phase: "restoring" | "confirming" | "leaving";
};

const UnsavedChangesContext = createContext<UnsavedChangesContextValue | null>(null);

export function UnsavedChangesProvider({ children }: { children: ReactNode }) {
  const requestConfirmation = useConfirmation();
  const location = useLocation();
  const entriesRef = useRef<Map<string, UnsavedChangesEntry>>(new Map());
  const activeEntryRef = useRef<UnsavedChangesEntry | null>(null);
  const historyIndexRef = useRef(readHistoryIndex(window.history.state));
  const pendingHistoryNavigationRef = useRef<PendingHistoryNavigation | null>(null);
  const [activeEntry, setActiveEntry] = useState<UnsavedChangesEntry | null>(null);

  const publishActiveEntry = useCallback(() => {
    let nextActiveEntry: UnsavedChangesEntry | null = null;
    entriesRef.current.forEach((entry) => {
      if (entry.isDirty) {
        nextActiveEntry = entry;
      }
    });

    activeEntryRef.current = nextActiveEntry;
    setActiveEntry(nextActiveEntry);
  }, []);

  const setEntry = useCallback(
    (entry: UnsavedChangesEntry) => {
      entriesRef.current.set(entry.id, {
        ...entry,
        message: normalizeUnsavedChangesMessage(entry.message),
      });
      publishActiveEntry();
    },
    [publishActiveEntry],
  );

  const removeEntry = useCallback(
    (id: string) => {
      entriesRef.current.delete(id);
      publishActiveEntry();
    },
    [publishActiveEntry],
  );

  const confirmEntryDiscardChanges = useCallback(async (
    entry: UnsavedChangesEntry,
    actionLabel?: string,
  ) => {
    if (!entry.isDirty) {
      return true;
    }

    return requestConfirmation({
      title: "放弃未保存的修改？",
      description: normalizeUnsavedChangesMessage(entry.message),
      details: [actionLabel?.trim() ? `继续${actionLabel.trim()}会丢失这些修改。` : "继续操作会丢失这些修改。"],
      confirmLabel: actionLabel?.trim() || "放弃修改",
      tone: "warning",
    });
  }, [requestConfirmation]);

  const confirmDiscardChanges = useCallback(async (actionLabel?: string) => {
    const entry = activeEntryRef.current;
    return entry ? confirmEntryDiscardChanges(entry, actionLabel) : true;
  }, [confirmEntryDiscardChanges]);

  useEffect(() => {
    function handleBeforeUnload(event: BeforeUnloadEvent) {
      if (!activeEntryRef.current) {
        return;
      }

      event.preventDefault();
      event.returnValue = "";
    }

    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, []);

  useEffect(() => {
    async function handleDocumentClick(event: MouseEvent) {
      const entry = activeEntryRef.current;
      if (!entry || !shouldCheckAnchorNavigation(event)) {
        return;
      }

      const anchor = findClosestAnchor(event.target);
      if (!anchor || !isHashRouterNavigation(anchor) || isCurrentLocation(anchor.href)) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      if (await confirmDiscardChanges("离开当前编辑页")) {
        window.location.href = anchor.href;
      }
    }

    document.addEventListener("click", handleDocumentClick, true);
    return () => document.removeEventListener("click", handleDocumentClick, true);
  }, [confirmDiscardChanges]);

  useEffect(() => {
    // HashRouter records an `idx` value in history.state. Keep the last fully
    // accepted entry so a browser Back/Forward action can be reversed before
    // asking the asynchronous in-app confirmation question.
    if (!pendingHistoryNavigationRef.current) {
      historyIndexRef.current = readHistoryIndex(window.history.state);
    }
  }, [location.hash, location.key, location.pathname, location.search]);

  useEffect(() => {
    function handlePopState(event: PopStateEvent) {
      const targetIndex = readHistoryIndex(event.state);
      const pending = pendingHistoryNavigationRef.current;

      if (pending) {
        if (pending.phase === "restoring" && targetIndex === pending.originIndex) {
          historyIndexRef.current = pending.originIndex;
          pending.phase = "confirming";
          void confirmDiscardChanges("离开当前编辑页").then((confirmed) => {
            if (pendingHistoryNavigationRef.current !== pending) {
              return;
            }

            if (!confirmed) {
              pendingHistoryNavigationRef.current = null;
              return;
            }

            pending.phase = "leaving";
            window.history.go(pending.delta);
          });
          return;
        }

        if (pending.phase === "leaving" && targetIndex === pending.targetIndex) {
          historyIndexRef.current = pending.targetIndex;
          pendingHistoryNavigationRef.current = null;
        }
        return;
      }

      const originIndex = historyIndexRef.current;
      if (!activeEntryRef.current || originIndex == null || targetIndex == null) {
        historyIndexRef.current = targetIndex;
        return;
      }

      const delta = targetIndex - originIndex;
      if (delta === 0) {
        return;
      }

      pendingHistoryNavigationRef.current = {
        originIndex,
        targetIndex,
        delta,
        phase: "restoring",
      };
      window.history.go(-delta);
    }

    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, [confirmDiscardChanges]);

  const value = useMemo(
    () => ({
      confirmDiscardChanges,
      confirmEntryDiscardChanges,
      hasUnsavedChanges: Boolean(activeEntry),
      removeEntry,
      setEntry,
    }),
    [activeEntry, confirmDiscardChanges, confirmEntryDiscardChanges, removeEntry, setEntry],
  );

  return <UnsavedChangesContext.Provider value={value}>{children}</UnsavedChangesContext.Provider>;
}

export function useUnsavedChangesGuard({ isDirty, message = defaultUnsavedChangesMessage }: UnsavedChangesGuardOptions) {
  const id = useId();
  const context = useContext(UnsavedChangesContext);
  if (!context) {
    throw new Error("useUnsavedChangesGuard must be used within UnsavedChangesProvider.");
  }

  const { confirmEntryDiscardChanges, removeEntry, setEntry } = context;

  useEffect(() => {
    setEntry({
      id,
      isDirty,
      message,
    });

    return () => removeEntry(id);
  }, [id, isDirty, message, removeEntry, setEntry]);

  const confirmDiscardChanges = useCallback(
    (actionLabel?: string) => confirmEntryDiscardChanges({ id, isDirty, message }, actionLabel),
    [confirmEntryDiscardChanges, id, isDirty, message],
  );

  return {
    confirmDiscardChanges,
    hasUnsavedChanges: isDirty,
  };
}

export function useConfirmUnsavedChanges() {
  const context = useContext(UnsavedChangesContext);
  if (!context) {
    throw new Error("useConfirmUnsavedChanges must be used within UnsavedChangesProvider.");
  }

  return context.confirmDiscardChanges;
}

export function useHasUnsavedChanges() {
  const context = useContext(UnsavedChangesContext);
  if (!context) {
    throw new Error("useHasUnsavedChanges must be used within UnsavedChangesProvider.");
  }

  return context.hasUnsavedChanges;
}

function normalizeUnsavedChangesMessage(message: string) {
  return message.trim() || defaultUnsavedChangesMessage;
}

function readHistoryIndex(state: unknown) {
  if (!state || typeof state !== "object" || !("idx" in state)) {
    return null;
  }

  const index = (state as { idx?: unknown }).idx;
  return typeof index === "number" && Number.isInteger(index) ? index : null;
}

function shouldCheckAnchorNavigation(event: MouseEvent) {
  return (
    event.button === 0 &&
    !event.defaultPrevented &&
    !event.altKey &&
    !event.ctrlKey &&
    !event.metaKey &&
    !event.shiftKey
  );
}

function findClosestAnchor(target: EventTarget | null) {
  if (!(target instanceof Element)) {
    return null;
  }

  return target.closest("a[href]") as HTMLAnchorElement | null;
}

function isHashRouterNavigation(anchor: HTMLAnchorElement) {
  if (anchor.hasAttribute("download")) {
    return false;
  }

  const target = anchor.getAttribute("target");
  if (target && target !== "_self") {
    return false;
  }

  const url = new URL(anchor.href);
  return (
    url.origin === window.location.origin &&
    url.pathname === window.location.pathname &&
    url.search === window.location.search &&
    url.hash.startsWith("#/")
  );
}

function isCurrentLocation(href: string) {
  const url = new URL(href);
  return (
    url.origin === window.location.origin &&
    url.pathname === window.location.pathname &&
    url.search === window.location.search &&
    url.hash === window.location.hash
  );
}
