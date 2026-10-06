import { useCallback, useEffect, useRef } from "react";

export function isAbortError(error: unknown): boolean {
  return Boolean(error && typeof error === "object" && "name" in error && (error as { name?: unknown }).name === "AbortError");
}

export function useAbortableOperation(scope?: unknown) {
  const activeControllers = useRef(new Set<AbortController>());

  useEffect(() => () => {
    activeControllers.current.forEach((controller) => controller.abort());
    activeControllers.current.clear();
  }, [scope]);

  return useCallback(async <T>(operation: (signal: AbortSignal) => Promise<T>) => {
    const controller = new AbortController();
    activeControllers.current.add(controller);
    try {
      const result = await operation(controller.signal);
      // A platform operation or already-resolved response may ignore cancellation.
      controller.signal.throwIfAborted();
      return result;
    } finally {
      activeControllers.current.delete(controller);
    }
  }, []);
}
