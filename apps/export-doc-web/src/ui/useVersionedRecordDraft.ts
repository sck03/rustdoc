import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { useServerDraftSync } from "./serverDraftSync.tsx";
import { mergeSavedDraftFields } from "./recordDraftMerge.ts";

/** Keeps editable values, the saved baseline and its concurrency token together. */
export function useVersionedRecordDraft<T extends { id: number; versionNumber: number }, K extends keyof T & string>(
  resourceKey: string,
  incoming: T | undefined,
  fields: readonly K[],
) {
  type Values = Record<K, string>;
  const valuesFrom = (record?: T) => Object.fromEntries(fields.map(key => [key, String(record?.[key] ?? "")])) as Values;
  type Draft = { resourceKey: string; adoptedFrom?: string; record?: T; values: Values; baseline: Values; persisted?: T };
  const initial = (): Draft => ({ resourceKey, record: incoming, values: valuesFrom(incoming), baseline: valuesFrom(incoming) });
  const [loaded, setLoaded] = useState<Draft>(initial);
  const belongsToResource = loaded.resourceKey === resourceKey || loaded.adoptedFrom === resourceKey;
  const current = belongsToResource ? loaded : initial();
  const currentRef = useRef(current);
  currentRef.current = current;
  const update = (next: Draft) => { currentRef.current = next; setLoaded(next); };
  const isDirty = fields.some(key => current.values[key] !== current.baseline[key]);
  useEffect(() => {
    if (!belongsToResource) setLoaded(current);
    else if (loaded.adoptedFrom && loaded.resourceKey === resourceKey) setLoaded({ ...loaded, adoptedFrom: undefined });
  }, [resourceKey, belongsToResource, loaded]);
  const sync = useServerDraftSync({
    resourceKey: current.resourceKey,
    incomingValue: current.resourceKey === resourceKey ? { record: incoming } : undefined,
    persistedValue: current.persisted ? { record: current.persisted } : undefined,
    isDirty,
    fingerprint: ({ record }) => record ? `${record.id}:${record.versionNumber}` : "missing",
    applyIncoming: ({ record }) => update({ resourceKey, record, values: valuesFrom(record), baseline: valuesFrom(record) }),
  });

  function acceptSaved(record: T, submitted: Values, nextResourceKey = resourceKey) {
    const latest = currentRef.current;
    const baseline = valuesFrom(record);
    const values = mergeSavedDraftFields(latest.values, submitted, baseline, fields);
    update({ resourceKey: nextResourceKey, adoptedFrom: nextResourceKey === resourceKey ? undefined : resourceKey, record, persisted: record, baseline, values });
  }
  function field(key: K) {
    return { value: current.values[key], onChange: (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      const latest = currentRef.current;
      update({ ...latest, values: { ...latest.values, [key]: event.currentTarget.value } });
    } };
  }
  return { record: current.record, values: current.values, isDirty, field, acceptSaved,
    reset: () => update({ ...currentRef.current, values: currentRef.current.baseline }), ...sync };
}
