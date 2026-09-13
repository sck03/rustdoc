import { useState } from "react";
import { useServerDraftSync } from "./serverDraftSync.tsx";

/** Keeps the form's loaded record and concurrency token together during refreshes. */
export function useVersionedRecordDraft<T extends { id: number; versionNumber: number }>(
  resourceKey: string,
  incoming: T | undefined,
  isDirty: boolean,
) {
  const [loaded, setLoaded] = useState({ resourceKey, record: incoming });
  const sync = useServerDraftSync({
    resourceKey,
    incomingValue: { record: incoming },
    isDirty,
    fingerprint: ({ record }) => record ? `${record.id}:${record.versionNumber}` : "missing",
    applyIncoming: ({ record }) => setLoaded({ resourceKey, record }),
  });
  return { record: loaded.resourceKey === resourceKey ? loaded.record : incoming, ...sync };
}
