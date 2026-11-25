import { useMemo, useRef, useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiSupplierContactDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useVersionedRecordDraft } from "../../ui/useVersionedRecordDraft.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useScopedConfirmation } from "../../ui/useScopedConfirmation.ts";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function useSupplierContacts(client: ExportDocManagerApiClient, supplierId: number,
  access: { create: boolean; edit: boolean; setPrimary: boolean; delete: boolean }) {
  const cache = useQueryClient();
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selection, setSelection] = useState<ApiSupplierContactDto | null | undefined>();
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const query = useQuery({
    ...largeDocumentQueryOptions,
    queryKey: ["supplier-contacts", supplierId, pageNumber, pageSize],
    queryFn: ({ signal }) => client.querySupplierContacts({ supplierId, pageNumber, pageSize }, { signal }),
  });
  useServerPageNumber(query, pageNumber, setPageNumber, pageSize);
  const contactId = selection?.id ?? 0;
  const record = query.data?.items.find(item => item.id === contactId) ?? selection ?? undefined;
  const draft = useVersionedRecordDraft(`supplier:${supplierId}:contact:${contactId}`, record, ["name", "title", "email", "phone", "instantMessaging"]);
  const scope = `supplier:${supplierId}:contact:${contactId}:${selection === undefined ? "directory" : "editor"}`;
  const confirmationScope = useMemo(() => ({ client, scope }), [client, scope]);
  const requestConfirmation = useConfirmation();
  const { confirmDiscardChanges: discard } = useUnsavedChangesGuard({ isDirty: draft.isDirty, message: "当前联系人资料有未保存的修改。" });
  const confirmDiscard = useScopedConfirmation(confirmationScope, discard);
  const activeAction = useRef<"save" | "primary" | "delete" | null>(null);
  const command = useScopedMutation<boolean, "save" | "primary" | "delete">(scope, client, {
    mutationFn: async (action, signal, { checkCurrent, continueInScope }) => {
      activeAction.current = action;
      const current = draft.record;
      if (action === "save" ? (current ? !access.edit : !access.create) : !current || !(action === "primary" ? access.setPrimary : access.delete)) return false;
      if (action !== "save") {
        if (action === "delete" && !await requestConfirmation({ title: "删除供应商联系人", description: `确定删除联系人“${current!.name}”吗？`, confirmLabel: "确认删除", tone: "danger" })) return false;
        checkCurrent();
        if (!await confirmDiscard(action === "delete" ? "删除联系人" : "设为主要联系人")) return false;
        checkCurrent();
      }
      const submitted = { ...draft.values };
      if (action === "delete") {
        await client.deleteSupplierContact({ supplierId, id: current!.id, expectedVersion: current!.versionNumber }, { signal });
        checkCurrent(); continueInScope(`supplier:${supplierId}:contact:0:directory`);
        draft.reset(); setSelection(undefined);
      } else {
        const id = current?.id ?? 0;
        const body = { id, supplierCompanyId: supplierId,
          ...Object.fromEntries(Object.entries(submitted).map(([key, value]) => [key, value.trim()])) as typeof submitted,
          expectedVersion: current?.versionNumber ?? 0 };
        const saved = action === "primary"
          ? await client.setPrimarySupplierContact({ supplierId, id, body: { expectedVersion: current!.versionNumber } }, { signal })
          : id ? await client.updateSupplierContact({ supplierId, id, body }, { signal }) : await client.createSupplierContact({ supplierId, body }, { signal });
        checkCurrent(); continueInScope(`supplier:${supplierId}:contact:${saved.id}:editor`);
        // Cancel an older list response before publishing the accepted version.
        await cache.cancelQueries({ queryKey: ["supplier-contacts", supplierId] }); checkCurrent();
        cache.setQueryData<typeof query.data>(["supplier-contacts", supplierId, pageNumber, pageSize], page => page && ({
          ...page, items: page.items.map(item => item.id === saved.id ? saved : action === "primary" ? { ...item, isPrimary: false } : item),
        }));
        draft.acceptSaved(saved, submitted, `supplier:${supplierId}:contact:${saved.id}`);
        setSelection(saved);
      }
      setFeedback(successFeedback(action === "delete" ? "联系人已删除。" : action === "primary" ? "主要联系人已切换。" : current ? "联系人已更新。" : "联系人已添加。"));
      return true;
    },
    refreshKeys: changed => changed ? [["supplier-contacts", supplierId]] : [],
    onRefreshError: error => setFeedback(warningFeedback(`联系人已保存，但目录刷新失败：${readApiError(error)}`)),
    onError: error => setFeedback(requestErrorFeedback(error)),
  });
  async function select(next: ApiSupplierContactDto | null | undefined) {
    if (command.isPending || !await confirmDiscard("切换联系人视图")) return;
    draft.reset(); setSelection(next); setFeedback(null);
  }
  return { query, draft, selection, feedback, command: { ...command, isEditingLocked: command.isPending && activeAction.current !== "save" }, pageNumber, pageSize, setPageNumber,
    setPageSize: (size: number) => { setPageSize(size); setPageNumber(1); }, select,
    save: (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); command.mutate("save"); },
    loadServer: async () => { if (await confirmDiscard("载入服务器版本")) draft.loadServerVersion(); },
  };
}
