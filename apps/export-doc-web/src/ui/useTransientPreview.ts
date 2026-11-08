import { useCallback, useLayoutEffect, useRef, useState } from "react";
import { isAbortError, useAbortableOperationController } from "./useAbortableOperation.ts";

/** Large previews live only with their current source, never in the mutation cache. */
export function useTransientPreview<T, Input = void>(
  scope: unknown,
  operation: (signal: AbortSignal, input: Input) => Promise<T>,
  onError: (error: unknown) => void,
  onSuccess?: () => void,
) {
  const { run, cancel } = useAbortableOperationController(scope, { cancelPrevious: true });
  const sequence = useRef(0);
  const latestScope = useRef(scope);
  latestScope.current = scope;
  const [state, setState] = useState<{ scope: unknown; value: T | null; pending: boolean } | null>(null);
  const clear = useCallback(() => {
    sequence.current += 1;
    cancel();
    setState(null);
  }, [cancel]);
  useLayoutEffect(() => {
    clear();
    return cancel;
  }, [scope, clear, cancel]);

  async function generate(input: Input) {
    const request = ++sequence.current;
    const current = () => request === sequence.current && Object.is(scope, latestScope.current);
    setState({ scope, value: null, pending: true });
    try {
      const value = await run(signal => operation(signal, input));
      if (!current()) return;
      setState({ scope, value, pending: false });
      onSuccess?.();
    } catch (error) {
      if (!current()) return;
      setState(null);
      if (!isAbortError(error)) onError(error);
    }
  }
  const current = state && Object.is(scope, state.scope) ? state : null;
  return { preview: current?.value ?? null, isPending: current?.pending ?? false, generate, clear };
}
