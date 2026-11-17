import { useMemo, type FormEvent } from "react";
import type { ApiCrmFollowUpDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { useScopedSave } from "../../../ui/useScopedSave.ts";
import { useVersionedRecordDraft } from "../../../ui/useVersionedRecordDraft.ts";
import { isAbortError } from "../../../ui/useAbortableOperation.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../../ui/businessTime.ts";
import { requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../../ui/OperationFeedback.tsx";

const fields = ["type", "summary", "nextAction", "nextFollowUpAt", "crmContactId"] as const;
export function useFollowUpCommands(options: {
  client: ExportDocManagerApiClient; view: string; customerId: number; recordId: number | null;
  record: ApiCrmFollowUpDto | null; transfer: boolean; timeZone: string;
  permissions: { create: boolean; edit: boolean; complete: boolean; restore: boolean; assign: boolean; delete: boolean };
  accept(record: ApiCrmFollowUpDto): void; complete(): void; refresh(): void;
  feedback(value: OperationFeedbackState | null): void;
}) {
  const { client, view, customerId, recordId, record, transfer, permissions, timeZone } = options;
  const owner = useMemo(() => ({}), [client, permissions.create, permissions.edit, permissions.complete, permissions.restore, permissions.assign, permissions.delete]);
  const scopeFor = (id: number | null) => `${view}:${customerId}:${id ?? "new"}:${transfer}`;
  const save = useScopedSave(scopeFor(recordId), owner);
  const incoming = useMemo(() => record ? { ...record, nextFollowUpAt: toBusinessDateTimeLocalInput(record.nextFollowUpAt, timeZone) } : undefined, [record, timeZone]);
  const draftKey = (id: number | null) => `${view}:${transfer}:followup:${id ?? "new"}`;
  const draft = useVersionedRecordDraft(draftKey(recordId), incoming, fields);
  const confirmation = useConfirmation();
  async function run(work: Parameters<typeof save.run>[0]) {
    try { await save.run(work); }
    catch (error) { if (!isAbortError(error)) options.feedback(requestErrorFeedback(error)); }
  }
  function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (record ? !permissions.edit : !permissions.create) return;
    if (!customerId) { options.feedback(warningFeedback("请先选择销售客户。")); return; }
    const submitted = { ...draft.values };
    void run(async ({ signal, checkCurrent, continueInScope }) => {
      options.feedback(null);
      const body = {
        id: record?.id ?? 0, crmCustomerId: customerId, crmContactId: Number(submitted.crmContactId) || undefined,
        type: submitted.type || "邮件", summary: submitted.summary, nextAction: submitted.nextAction,
        nextFollowUpAt: businessDateTimeLocalInputToIso(submitted.nextFollowUpAt, timeZone),
        expectedVersion: draft.record?.versionNumber ?? 0,
      };
      const saved = record
        ? await client.updateCrmFollowUp({ id: record.id, body: { ...body, followedUpAt: record.followedUpAt } }, { signal })
        : await client.createCrmFollowUp({ body }, { signal });
      checkCurrent();
      continueInScope(scopeFor(saved.id));
      const continued = draft.acceptSaved({ ...saved, nextFollowUpAt: toBusinessDateTimeLocalInput(saved.nextFollowUpAt, timeZone) }, submitted, draftKey(saved.id));
      options.accept(saved);
      options.feedback(successFeedback("客户跟进已保存。"));
      options.refresh();
      if (!continued) options.complete();
    });
  }
  function toggleCompleted(item: ApiCrmFollowUpDto) {
    if (item.isCompleted ? !permissions.restore : !permissions.complete) return;
    return run(async ({ signal, checkCurrent }) => {
      const request = { id: item.id, body: { expectedVersion: item.versionNumber } };
      if (item.isCompleted) await client.restoreCrmFollowUp(request, { signal });
      else await client.completeCrmFollowUp(request, { signal });
      checkCurrent(); options.refresh();
      options.feedback(successFeedback(item.isCompleted ? "跟进记录已恢复为待跟进。" : "跟进记录已标记完成。"));
    });
  }
  function transferFollowUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!record || !permissions.assign || !customerId) return;
    if (record.crmCustomerId === customerId) { options.feedback(warningFeedback("请选择不同的目标客户。")); return; }
    const contactId = Number(draft.values.crmContactId) || null;
    void run(async ({ signal, checkCurrent }) => {
      const confirmed = await confirmation({ title: "转移跟进记录", description: "确认将此跟进转移到当前选择的客户？", confirmLabel: "确认转移", tone: "warning" });
      checkCurrent(); if (!confirmed) return;
      await client.transferCrmFollowUp({ id: record.id, body: { crmCustomerId: customerId, crmContactId: contactId, expectedVersion: record.versionNumber } }, { signal });
      checkCurrent(); options.feedback(successFeedback("跟进记录已转移。")); options.refresh(); options.complete();
    });
  }
  function deleteFollowUp(item: ApiCrmFollowUpDto) {
    if (!permissions.delete) return;
    return run(async ({ signal, checkCurrent }) => {
      const confirmed = await confirmation({ title: "删除跟进记录", description: `确定删除“${item.customerName}”的这条跟进记录吗？`, confirmLabel: "确认删除", tone: "danger" });
      checkCurrent(); if (!confirmed) return;
      await client.deleteCrmFollowUp({ id: item.id, expectedVersion: item.versionNumber }, { signal });
      checkCurrent(); options.refresh(); options.feedback(successFeedback("跟进记录已删除。"));
    });
  }
  return { draft, saving: save.isSaving, handleCreate, toggleCompleted, transferFollowUp, deleteFollowUp };
}
