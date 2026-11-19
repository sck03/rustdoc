import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useVersionedRecordDraft } from "../../ui/useVersionedRecordDraft.ts";
import { useScopedConfirmation } from "../../ui/useScopedConfirmation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import type { OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";

type RecordVersion = { id: number; versionNumber: number };
type Page<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number };
export type SupplierRelatedView = "directory" | "editor" | "analytics";

/** Bounded directory, record draft and selection shared by supplier workspaces. */
export function useSupplierRelatedWorkspace<T extends RecordVersion, K extends keyof T & string>(
  client: ExportDocManagerApiClient, supplierId: number, kind: string, accessKey: string,
  fields: readonly K[], empty: T,
  load: (pageNumber: number, pageSize: number, signal: AbortSignal) => Promise<Page<T>>,
) {
  const cache = useQueryClient();
  const queryScope = useMemo(createRequestKey, [client, accessKey]);
  const identity = `${kind}:${supplierId}:${queryScope}`;
  const [selection, setSelection] = useState<{ identity: string; record: T | null; view: SupplierRelatedView }>();
  const current = selection?.identity === identity ? selection : undefined;
  const view = current?.view ?? "directory";
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [feedbackState, updateFeedback] = useState<{ identity: string; value: OperationFeedbackState | null }>();
  const feedback = feedbackState?.identity === identity ? feedbackState.value : null;
  const setFeedback = (value: OperationFeedbackState | null) => updateFeedback({ identity, value });
  const queryKey = [kind, supplierId, queryScope, pageNumber, pageSize] as const;
  const query = useQuery({ ...largeDocumentQueryOptions, queryKey,
    queryFn: ({ signal }) => load(pageNumber, pageSize, signal) });
  useServerPageNumber(query, pageNumber, setPageNumber, pageSize);
  const id = current?.record?.id ?? 0;
  const incoming = query.data?.items.find(row => row.id === id) ?? current?.record ?? empty;
  const resourceKey = (recordId: number) => `${identity}:${recordId}`;
  const draft = useVersionedRecordDraft(resourceKey(id), incoming, fields);
  const scopeFor = (recordId: number, nextView: SupplierRelatedView) => `${resourceKey(recordId)}:${nextView}`;
  const scope = scopeFor(id, view);
  const owner = useMemo(() => ({ client, accessKey }), [client, accessKey]);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: view === "editor" && draft.isDirty });
  const discard = useScopedConfirmation(useMemo(() => ({ scope, owner }), [scope, owner]), confirmDiscardChanges);
  async function select(record: T | null, nextView: SupplierRelatedView = "editor") {
    if (!await discard("切换供应商工作区")) return;
    draft.reset(); setSelection({ identity, record, view: nextView }); setFeedback(null);
  }
  async function acceptSaved(saved: T, submitted: typeof draft.values, checkCurrent: () => void) {
    await cache.cancelQueries({ queryKey: [kind, supplierId] });
    checkCurrent();
    cache.setQueryData<Page<T>>(queryKey, page => page && ({ ...page,
      items: page.items.map(row => row.id === saved.id ? saved : row) }));
    draft.acceptSaved(saved, submitted, resourceKey(saved.id));
    setSelection({ identity, record: saved, view });
  }
  function acceptDeleted() {
    cache.setQueryData<Page<T>>(queryKey, page => page && ({ ...page, items: page.items.filter(row => row.id !== id) }));
    draft.reset(); setSelection({ identity, record: null, view: "directory" });
  }
  return { query, queryScope, draft, view, scope, scopeFor, owner, feedback, setFeedback, discard, select,
    acceptSaved, acceptDeleted, refreshKeys: [[kind, supplierId]] as const,
    pageNumber, pageSize, setPageNumber, setPageSize: (size: number) => { setPageSize(size); setPageNumber(1); },
    loadServer: async () => { if (await discard("载入服务器版本")) draft.loadServerVersion(); } };
}
