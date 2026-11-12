import { useLayoutEffect, useRef } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";
import { isAbortError } from "./useAbortableOperation.ts";
import { useScopedSave } from "./useScopedSave.ts";
import { readApiError } from "./formUtils.ts";

/** Commands keep no input/result cache; refreshes do not extend the write lock. */
export function useScopedMutation<Result, Input = void>(
  scope: string,
  owner: unknown,
  options: {
    mutationFn(
      input: Input,
      signal: AbortSignal,
      context: { checkCurrent(): void; continueInScope(next: string): void },
    ): Promise<Result>;
    onSuccess?(result: Result, input: Input): void;
    refreshKeys?(result: Result, input: Input): readonly QueryKey[];
    refresh?(result: Result, input: Input): Promise<unknown> | undefined;
    onRefreshError?(error: unknown): void;
    onError(error: unknown): void;
  },
) {
  const save = useScopedSave(scope, owner);
  const cache = useQueryClient();
  const generation = useRef(0);
  const admitted = useRef<{ scopes: Set<string>; owner: unknown } | null>(null);
  const latest = useRef({ scope, owner });
  latest.current = { scope, owner };
  useLayoutEffect(() => {
    if (!admitted.current || admitted.current.owner !== owner || !admitted.current.scopes.has(scope))
      generation.current += 1;
  }, [scope, owner]);
  useLayoutEffect(
    () => () => {
      generation.current += 1;
    },
    [],
  );

  async function mutateAsync(input: Input) {
    try {
      await save.run(async ({ signal, checkCurrent, continueInScope }) => {
        const submitted = ++generation.current;
        const scopes = new Set([scope]);
        admitted.current = { scopes, owner };
        const result = await options.mutationFn(input, signal, {
          checkCurrent,
          continueInScope: (next) => {
            scopes.add(next);
            continueInScope(next);
          },
        });
        checkCurrent();
        const keys = options.refreshKeys?.(result, input) ?? [];
        options.onSuccess?.(result, input);
        // Start every refresh before yielding; an old command cannot invalidate a new session.
        const refreshes = keys.map((queryKey) => cache.invalidateQueries({ queryKey }, { throwOnError: true }));
        // Convert synchronous callback failures into refresh failures too.
        try {
          refreshes.push(Promise.resolve(options.refresh?.(result, input)).then(() => {}));
        } catch (error) {
          refreshes.push(Promise.reject(error));
        }
        void Promise.all(refreshes).catch((error) => {
          if (
            submitted === generation.current &&
            scopes.has(latest.current.scope) &&
            owner === latest.current.owner &&
            !isAbortError(error)
          ) {
            if (options.onRefreshError) options.onRefreshError(error);
            else options.onError(new Error(`操作已完成，刷新失败：${readApiError(error)}`));
          }
        });
      });
    } catch (error) {
      if (!isAbortError(error)) options.onError(error);
    }
  }
  return {
    mutate: (input: Input) => {
      void mutateAsync(input);
    },
    mutateAsync,
    cancel: () => {
      generation.current += 1;
      admitted.current = null;
      save.cancel();
    },
    isPending: save.isSaving,
  };
}
