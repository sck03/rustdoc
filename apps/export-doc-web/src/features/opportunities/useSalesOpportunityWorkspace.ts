import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiSalesOpportunityDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import { usePagedDirectoryQuery } from "../../ui/usePagedDirectoryQuery.ts";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteId } from "../../ui/routeQueryState.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useVersionedRecordDraft } from "../../ui/useVersionedRecordDraft.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { requestErrorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { opportunityClosed, opportunityDetailKey, opportunityDraftKey, opportunityFields, opportunityRecord, opportunityRequest, type OpportunityValues } from "./salesOpportunityModel.ts";

export function useSalesOpportunityWorkspace(client: ExportDocManagerApiClient) {
  const permissions = {
    create: usePermission(permissionResources.salesOpportunities, permissionActions.create).allowed,
    edit: usePermission(permissionResources.salesOpportunities, permissionActions.edit).allowed,
    transition: usePermission(permissionResources.salesOpportunities, permissionActions.transition).allowed,
    archive: usePermission(permissionResources.salesOpportunities, permissionActions.archive).allowed,
    createQuote: usePermission(permissionResources.salesQuotes, permissionActions.create).allowed,
    editQuote: usePermission(permissionResources.salesQuotes, permissionActions.edit).allowed,
    createCustomer: usePermission(permissionResources.crmCustomers, permissionActions.create).allowed,
  };
  const owner = useMemo(() => ({}), [client, ...Object.values(permissions)]);
  const queryScope = useMemo(createRequestKey, [client]);
  const directory = useDirectoryLocation();
  const { params, update: updateRoute } = useRouteQuery();
  const id = readRouteId(params.get("opportunityId"));
  const view = params.get("view") === "history" ? "history" : params.get("view") === "editor" ? "editor" : "directory";
  const includeArchived = params.get("archived") === "true";
  const scopeFor = (recordId: number | null, nextView: string) => `${recordId ?? "new"}:${nextView}`;
  const cache = useQueryClient();
  const navigate = useNavigate();
  const page = usePagedDirectoryQuery(
    ["sales-opportunities", "page", queryScope, directory.keyword, directory.status, directory.pageNumber, directory.pageSize, includeArchived],
    (signal) => client.querySalesOpportunities({ keyword: directory.keyword, stage: directory.status, pageNumber: directory.pageNumber,
      pageSize: directory.pageSize, includeArchived }, { signal }), view === "directory", { gcTime: 0 });
  useServerPageNumber(page, directory.pageNumber, directory.setPageNumber);
  const detail = useQuery({ queryKey: opportunityDetailKey(id, queryScope), queryFn: ({ signal }) => client.getSalesOpportunity({ id: id! }, { signal }), enabled: id !== null && view !== "directory", gcTime: 0 });
  const incoming = useMemo(() => detail.data ? opportunityRecord(detail.data) : undefined, [detail.data]);
  const draft = useVersionedRecordDraft(opportunityDraftKey(id), incoming, opportunityFields);
  const record = draft.record;
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [action, setAction] = useState("");
  const [transitionNote, setTransitionNote] = useState("");
  const confirmation = useConfirmation();
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: view === "editor" && draft.isDirty, message: "当前商机有未保存的修改。" });
  type Input = { kind: "save"; values: OpportunityValues } | { kind: "transition"; stage: string; note: string } |
    { kind: "archive" } | { kind: "navigate"; id: number | null; view: string } | { kind: "reload" } | { kind: "customer" };
  type Result = { kind: "saved"; record: ApiSalesOpportunityDto; submitted: OpportunityValues } | { kind: "changed"; record: ApiSalesOpportunityDto } |
    { kind: "navigate"; id: number | null; view: string } | { kind: "archived" } | { kind: "customer" } | null;
  const command = useScopedMutation<Result, Input>(scopeFor(id, view), owner, {
    mutationFn: async (input, signal, context) => {
      setAction(input.kind); setFeedback(null);
      if (input.kind === "customer") {
        if (!permissions.createCustomer) throw new Error("当前账号不能建立客户资料。");
        const accepted = await confirmDiscardChanges("打开客户资料");
        context.checkCurrent(); return accepted ? input : null;
      }
      if (input.kind === "navigate" || input.kind === "reload") {
        const accepted = await confirmDiscardChanges(input.kind === "reload" ? "重新加载商机" : "切换商机工作区");
        context.checkCurrent(); if (!accepted) return null;
        if (input.kind === "navigate") {
          context.continueInScope(scopeFor(input.id, input.view));
          return input;
        }
        if (id === null) return null;
        return { kind: "changed", record: await client.getSalesOpportunity({ id }, { signal }) };
      }
      if (id !== null && (!record || detail.isError)) throw new Error("请先重新加载当前商机。");
      if (input.kind === "save") {
        if (opportunityClosed(record) || !(record ? permissions.edit : permissions.create)) throw new Error("当前商机不能编辑。");
        const body = opportunityRequest(input.values, record, record ? permissions.editQuote : permissions.createQuote);
        const saved = record ? await client.updateSalesOpportunity({ id: record.id, body }, { signal }) : await client.createSalesOpportunity({ body }, { signal });
        context.continueInScope(scopeFor(saved.id, "editor"));
        return { kind: "saved", record: saved, submitted: input.values };
      }
      if (!record || record.isArchived || !(input.kind === "archive" ? permissions.archive : permissions.transition)) throw new Error("当前商机不能执行此操作。");
      const discard = await confirmDiscardChanges(input.kind === "archive" ? "归档商机" : "流转商机阶段");
      context.checkCurrent(); if (!discard) return null;
      const accepted = await confirmation({ title: input.kind === "archive" ? "归档商机" : "流转商机阶段",
        description: input.kind === "archive" ? `归档“${record.title}”？` : `将“${record.title}”流转到“${input.stage}”？`,
        confirmLabel: "确认", tone: input.kind === "archive" ? "danger" : "warning" });
      context.checkCurrent(); if (!accepted) return null;
      if (input.kind === "transition") return { kind: "changed", record: await client.transitionSalesOpportunity({ id: record.id,
        body: { nextStage: input.stage, changeNote: input.note, expectedVersion: record.versionNumber } }, { signal }) };
      await client.archiveSalesOpportunity({ id: record.id, body: { expectedVersion: record.versionNumber } }, { signal });
      context.continueInScope(scopeFor(null, "directory"));
      return { kind: "archived" };
    },
    onSuccess: result => {
      if (!result) return;
      if (result.kind === "customer") { draft.reset(); navigate("/crm/follow-ups?view=profile"); return; }
      if (result.kind === "navigate" || result.kind === "archived") {
        draft.reset(); setTransitionNote("");
        updateRoute({ opportunityId: result.kind === "navigate" ? result.id : null, view: result.kind === "navigate" ? result.view : "directory" }, false);
        if (result.kind === "archived") setFeedback(successFeedback("商机已归档，可勾选“含归档”查看历史。"));
        return;
      }
      cache.setQueryData(opportunityDetailKey(result.record.id, queryScope), result.record);
      if (result.kind === "saved") {
        const continued = draft.acceptSaved(opportunityRecord(result.record), result.submitted, opportunityDraftKey(result.record.id));
        updateRoute({ opportunityId: result.record.id, view: "editor" }, true);
        setFeedback(successFeedback(continued ? "商机已保存，后续修改仍在草稿中。" : "商机已保存。"));
      } else {
        draft.acceptServerVersion({ record: opportunityRecord(result.record) });
        setTransitionNote(""); setFeedback(successFeedback("商机已更新。"));
      }
    },
    refreshKeys: result => result && !["navigate", "customer"].includes(result.kind) ? [["sales-opportunities"], queryKeys.crmDashboard()] : [],
    onError: error => setFeedback(requestErrorFeedback(error)),
  });
  return { directory, id, view, includeArchived, permissions, page, detail, draft, record, feedback, transitionNote, setTransitionNote,
    busy: command.isPending, editLocked: command.isPending && action !== "save", client,
    navigate: (recordId: number | null, nextView: string) => command.mutate({ kind: "navigate", id: recordId, view: nextView }),
    save: () => command.mutate({ kind: "save", values: { ...draft.values } }), reload: () => command.mutate({ kind: "reload" }),
    transition: (stage: string) => command.mutate({ kind: "transition", stage, note: transitionNote.trim() }), archive: () => command.mutate({ kind: "archive" }),
    changeArchived: (value: boolean) => updateRoute({ archived: value, page: null }),
    openCustomer: () => command.mutate({ kind: "customer" }),
  };
}
export type SalesOpportunityWorkspace = ReturnType<typeof useSalesOpportunityWorkspace>;
