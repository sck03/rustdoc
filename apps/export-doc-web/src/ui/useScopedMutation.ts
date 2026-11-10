import { useLayoutEffect, useRef } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";
import { isAbortError } from "./useAbortableOperation.ts";
import { useScopedSave } from "./useScopedSave.ts";
import { readApiError } from "./formUtils.ts";

/** Commands keep no input/result cache; refreshes do not extend the write lock. */
export function useScopedMutation<Result, Input = void>(scope: string, owner: unknown, options: {
  mutationFn(input: Input, signal: AbortSignal): Promise<Result>;
  onSuccess?(result: Result, input: Input): void;
  refreshKeys?(result: Result, input: Input): readonly QueryKey[];
  onError(error: unknown): void;
}) {
  const save = useScopedSave(scope, owner);
  const cache = useQueryClient();
  const generation = useRef(0);
  const latest = useRef({ scope, owner });
  latest.current = { scope, owner };
  useLayoutEffect(() => () => { generation.current += 1; }, [scope, owner]);

  async function mutateAsync(input: Input) {
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        const submitted = ++generation.current;
        const result = await options.mutationFn(input, signal);
        checkCurrent();
        const keys = options.refreshKeys?.(result, input) ?? [];
        options.onSuccess?.(result, input);
        // Start every refresh before yielding; an old command cannot invalidate a new session.
        void Promise.all(keys.map(queryKey => cache.invalidateQueries({ queryKey }, { throwOnError: true })))
          .catch(error => {
            if (submitted === generation.current && scope === latest.current.scope && owner === latest.current.owner && !isAbortError(error)) {
              options.onError(new Error(`操作已完成，刷新失败：${readApiError(error)}`));
            }
          });
      });
    } catch (error) {
      if (!isAbortError(error)) options.onError(error);
    }
  }
  return { mutate: (input: Input) => { void mutateAsync(input); }, mutateAsync, isPending: save.isSaving };
}
