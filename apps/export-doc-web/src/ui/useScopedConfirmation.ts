import { isAbortError, useAbortableOperation } from "./useAbortableOperation.ts";

/** A dialog may outlive its caller; its answer must not start a stale command. */
export function useScopedConfirmation<Args extends unknown[]>(scope: unknown, requestConfirmation: (...args: Args) => Promise<boolean>) {
  const run = useAbortableOperation(scope);
  return async (...args: Args) => {
    try { return await run(async () => requestConfirmation(...args)); }
    catch (error) { if (isAbortError(error)) return false; throw error; }
  };
}
