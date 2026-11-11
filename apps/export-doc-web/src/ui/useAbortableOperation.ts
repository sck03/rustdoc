import { useCallback, useLayoutEffect, useRef } from "react";

export function isAbortError(error: unknown): boolean {
  return Boolean(error && typeof error === "object" && "name" in error && (error as { name?: unknown }).name === "AbortError");
}

export function useAbortableOperation(scope?: unknown, { cancelPrevious = false } = {}) {
  return useAbortableOperationController(scope, { cancelPrevious }).run;
}

export function useAbortableOperationController(scope?: unknown, { cancelPrevious = false } = {}) {
  const activeControllers = useRef(new Set<AbortController>());
  const latestScope = useRef(scope); latestScope.current = scope;
  const mounted = useRef(false);

  const cancel = useCallback(() => {
    activeControllers.current.forEach((controller) => controller.abort());
    activeControllers.current.clear();
  }, []);
  useLayoutEffect(() => cancel, [scope, cancel]);
  useLayoutEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; cancel(); };
  }, [cancel]);

  const run = useCallback(async <T>(operation: (signal: AbortSignal) => Promise<T>) => {
    if (!mounted.current || !Object.is(scope, latestScope.current)) {
      throw new DOMException("操作所属页面已变化。", "AbortError");
    }
    if (cancelPrevious) cancel();
    const controller = new AbortController();
    const submittedScope = latestScope.current;
    activeControllers.current.add(controller);
    try {
      return await operation(controller.signal);
    } finally {
      activeControllers.current.delete(controller);
      if (!Object.is(submittedScope, latestScope.current)) controller.abort();
      // Ignore both late success and late failure from operations that cannot cancel.
      controller.signal.throwIfAborted();
    }
  }, [scope, cancelPrevious, cancel]);
  return { run, cancel };
}
