import { useRef, useState, type FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useVersionedRecordDraft } from "../../ui/useVersionedRecordDraft.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { readApiError } from "../../ui/formUtils.ts";

type ProfileAction = "save" | "admit" | "deactivate" | "restore" | "delete";
const statusMessages = {
  admit: ["准入供应商", "确认该供应商已经完成准入审核并进入合作中状态吗？", "供应商已准入。"],
  deactivate: ["停用供应商", "停用会保留联系人、供货关系和评价历史，确定继续吗？", "供应商已停用。"],
  restore: ["恢复供应商", "恢复后供应商将回到考察中状态，确定继续吗？", "供应商已恢复考察。"],
} as const;

export function useSupplierProfile({ client, supplierId, newSupplier, view, access, confirmDiscard, onSelect }: {
  client: ExportDocManagerApiClient; supplierId: number; newSupplier: boolean; view: string;
  access: { create: boolean; edit: boolean; admit: boolean; deactivate: boolean; delete: boolean };
  confirmDiscard: (label: string) => Promise<boolean>;
  onSelect: (id: number, deleted: boolean) => void;
}) {
  const cache = useQueryClient(), requestConfirmation = useConfirmation();
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const query = useQuery({
    ...largeDocumentQueryOptions,
    queryKey: ["supplier", supplierId],
    queryFn: ({ signal }) => client.getSupplier({ id: supplierId }, { signal }),
    enabled: supplierId > 0 && !newSupplier && ["profile", "contacts", "products", "assessments"].includes(view),
  });
  const draft = useVersionedRecordDraft(`supplier:${supplierId}`, newSupplier ? undefined : query.data, ["name", "countryRegion", "category", "website", "mainProducts", "notes"]);
  const activeAction = useRef<ProfileAction | null>(null);
  const command = useScopedMutation<boolean, ProfileAction>(`supplier:${supplierId}:${view}`, client, {
    mutationFn: async (action, signal, { checkCurrent, continueInScope }) => {
      activeAction.current = action;
      const current = draft.record;
      if (action === "save" ? (newSupplier ? !access.create : !access.edit || !current)
        : !current || !(action === "delete" ? access.delete : action === "admit" ? access.admit : access.deactivate)) return false;
      if (action !== "save") {
        const [title, description] = action === "delete"
          ? ["删除供应商", `确定删除供应商“${current!.name}”吗？只有没有供货关系和评价的供应商才能删除，联系人会一并删除。`]
          : statusMessages[action];
        if (!await requestConfirmation({ title, description, confirmLabel: action === "delete" ? "确认删除" : title,
          tone: action === "delete" || action === "deactivate" ? "danger" : undefined })) return false;
        checkCurrent();
        if (!await confirmDiscard(title)) return false;
        checkCurrent();
      }
      const submitted = { ...draft.values }, id = newSupplier ? 0 : current?.id ?? 0;
      if (action === "delete") {
        await client.deleteSupplier({ id, expectedVersion: current!.versionNumber }, { signal });
        checkCurrent(); continueInScope("supplier:0:directory");
        draft.reset(); onSelect(0, true);
        cache.removeQueries({ queryKey: ["supplier", id], exact: true });
      } else {
        const request = { id, body: { expectedVersion: current?.versionNumber ?? 0 } };
        const body = { id, ...Object.fromEntries(Object.entries(submitted).map(([key, value]) => [key, value.trim()])) as typeof submitted,
          expectedVersion: current?.versionNumber ?? 0 };
        const saved = action === "save"
          ? id ? await client.updateSupplier({ id, body }, { signal }) : await client.createSupplier({ body }, { signal })
          : action === "admit" ? await client.admitSupplier(request, { signal })
          : action === "restore" ? await client.restoreSupplier(request, { signal }) : await client.deactivateSupplier(request, { signal });
        checkCurrent(); continueInScope(`supplier:${saved.id}:${view}`);
        await cache.cancelQueries({ queryKey: ["supplier", saved.id], exact: true }); checkCurrent();
        cache.setQueryData(["supplier", saved.id], saved);
        draft.acceptSaved(saved, submitted, `supplier:${saved.id}`);
        onSelect(saved.id, false);
      }
      setFeedback(successFeedback(action === "save" ? id ? "供应商已更新。" : "供应商已建立。"
        : action === "delete" ? "供应商已删除。" : statusMessages[action][2]));
      return true;
    },
    refreshKeys: changed => changed ? [["suppliers"]] : [],
    onRefreshError: error => setFeedback(warningFeedback(`供应商操作已完成，但目录刷新失败：${readApiError(error)}`)),
    onError: error => setFeedback(requestErrorFeedback(error)),
  });
  return { query, draft, command: { ...command, isEditingLocked: command.isPending && activeAction.current !== "save" }, feedback, setFeedback,
    save: (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); command.mutate("save"); } };
}
