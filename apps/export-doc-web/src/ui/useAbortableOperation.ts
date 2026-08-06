import { useCallback, useEffect, useRef } from "react";

export function useAbortableOperation() {
  const activeControllers = useRef(new Set<AbortController>());

  useEffect(() => () => {
    activeControllers.current.forEach((controller) => controller.abort());
    activeControllers.current.clear();
  }, []);

  return useCallback(async <T>(operation: (signal: AbortSignal) => Promise<T>) => {
    const controller = new AbortController();
    activeControllers.current.add(controller);
    try {
      return await operation(controller.signal);
    } finally {
      activeControllers.current.delete(controller);
    }
  }, []);
}
