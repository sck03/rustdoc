import { useLayoutEffect, useState } from "react";
import type { Announcement, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";

/** A new open intent invalidates the pending read before changing editor identity. */
export function useAnnouncementEditor(client: ExportDocManagerApiClient, scope: string) {
  const [editing, setEditing] = useState<Announcement | "new" | null>(null);
  const [error, setError] = useState("");
  const opening = useScopedMutation(scope, client, {
    mutationFn: (id: number, signal: AbortSignal) => client.getAnnouncement({ id }, { signal }),
    onSuccess: setEditing,
    onError: cause => setError(readApiError(cause)),
  });
  useLayoutEffect(() => { setEditing(null); setError(""); }, [scope, client]);
  function open(record: Announcement | "new" | null) {
    opening.cancel(); setError(""); setEditing(record);
  }
  return { editing, error, busy: opening.isPending, open,
    load(id: number) { setError(""); opening.mutate(id); } };
}
