import { useCallback, useLayoutEffect, useRef, useState } from "react";

/** Serializes a form's saves and rejects results after leaving its workspace. */
export function useScopedSave(scope: string, owner: unknown) {
  const latest = useRef({ scope, owner });
  latest.current = { scope, owner };
  const active = useRef<{ controller: AbortController; scopes: Set<string>; owner: unknown } | null>(null);
  const [isSaving, setSaving] = useState(false);
  const cancel = useCallback(() => {
    active.current?.controller.abort(); active.current = null; setSaving(false);
  }, []);
  useLayoutEffect(() => {
    if (active.current && (!active.current.scopes.has(scope) || active.current.owner !== owner)) {
      active.current.controller.abort(); active.current = null; setSaving(false);
    }
  }, [scope, owner]);
  useLayoutEffect(() => () => { active.current?.controller.abort(); active.current = null; }, []);

  async function run(operation: (context: { signal: AbortSignal; checkCurrent: () => void; continueInScope: (next: string) => void }) => Promise<void>) {
    if (active.current) return;
    const pending = { controller: new AbortController(), scopes: new Set([scope]), owner };
    active.current = pending; setSaving(true);
    const checkCurrent = () => {
      if (active.current !== pending || !pending.scopes.has(latest.current.scope) || pending.owner !== latest.current.owner) pending.controller.abort();
      pending.controller.signal.throwIfAborted();
    };
    try {
      await operation({ signal: pending.controller.signal, checkCurrent, continueInScope: next => pending.scopes.add(next) });
    } catch (error) {
      checkCurrent(); throw error;
    } finally {
      if (active.current === pending) { active.current = null; setSaving(false); }
    }
  }
  return { run, cancel, isSaving };
}
