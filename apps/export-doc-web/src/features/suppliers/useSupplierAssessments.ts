import { useRef, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useSupplierRelatedWorkspace, type SupplierRelatedView } from "./useSupplierRelatedWorkspace.ts";
import { ASSESSMENT_FIELDS, emptyAssessment, assessmentRequest } from "./supplierAssessmentModel.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { requestErrorFeedback, successFeedback, warningFeedback } from "../../ui/OperationFeedback.tsx";

type Action = "save" | "confirm" | "delete";
export function useSupplierAssessments(client: ExportDocManagerApiClient, supplierId: number, today: string,
  access: { create: boolean; edit: boolean; approve: boolean; delete: boolean }) {
  const workspace = useSupplierRelatedWorkspace(client, supplierId, "supplier-assessments", JSON.stringify(access),
    ASSESSMENT_FIELDS, emptyAssessment(supplierId, today),
    (pageNumber, pageSize, signal) => client.listSupplierAssessments({ supplierId, pageNumber, pageSize }, { signal }));
  const analyticsQuery = useQuery({ ...largeDocumentQueryOptions,
    queryKey: ["supplier-assessment-analytics", supplierId, workspace.queryScope],
    queryFn: ({ signal }) => client.getSupplierAssessmentAnalytics({ supplierId }, { signal }),
    enabled: workspace.view === "analytics" });
  const confirmAction = useConfirmation();
  const activeAction = useRef<Action>("save");
  const command = useScopedMutation<boolean, Action>(workspace.scope, workspace.owner, {
    mutationFn: async (action, signal, { checkCurrent, continueInScope }) => {
      activeAction.current = action;
      const record = workspace.draft.record!;
      if (record.status === "Confirmed" || (action === "save" ? record.id ? !access.edit : !access.create
        : !record.id || !(action === "confirm" ? access.approve : access.delete))) return false;
      if (action !== "save") {
        const label = action === "confirm" ? "确认供应商评价" : "删除供应商评价";
        if (!await confirmAction({ title: label, description: `${label} ${record.assessmentDate} 的记录吗？`,
          details: action === "confirm" ? ["确认后不能修改或删除；更正请新建复评。"] : undefined,
          confirmLabel: action === "confirm" ? "确认评价" : "确认删除", tone: action === "delete" ? "danger" : undefined })) return false;
        checkCurrent();
        if (!await workspace.discard(label)) return false;
        checkCurrent();
      }
      const submitted = { ...workspace.draft.values };
      if (action === "delete") {
        await client.deleteSupplierAssessment({ supplierId, id: record.id, expectedVersion: record.versionNumber }, { signal });
        checkCurrent(); continueInScope(workspace.scopeFor(0, "directory")); workspace.acceptDeleted();
      } else {
        const saved = action === "confirm"
          ? await client.confirmSupplierAssessment({ supplierId, id: record.id, expectedVersion: record.versionNumber }, { signal })
          : record.id ? await client.updateSupplierAssessment({ supplierId, id: record.id, body: assessmentRequest(record, submitted) }, { signal })
          : await client.createSupplierAssessment({ supplierId, body: assessmentRequest(record, submitted) }, { signal });
        checkCurrent(); continueInScope(workspace.scopeFor(saved.id, workspace.view));
        await workspace.acceptSaved(saved, submitted, checkCurrent);
      }
      workspace.setFeedback(successFeedback(action === "delete" ? "供应商评价已删除。" : action === "confirm" ? "供应商评价已确认并锁定。" : "供应商评价已保存。"));
      return true;
    },
    refreshKeys: changed => changed ? [...workspace.refreshKeys, ["supplier-assessment-analytics",supplierId], ["supplier-assessment-overview"]] : [],
    onRefreshError: error => workspace.setFeedback(warningFeedback(`操作已完成，列表刷新失败：${readApiError(error)}`)),
    onError: error => workspace.setFeedback(requestErrorFeedback(error)),
  });
  const select: typeof workspace.select = (...args) => command.isPending ? Promise.resolve() : workspace.select(...args);
  return { ...workspace, analyticsQuery, select,
    command: { ...command, isEditingLocked: command.isPending && activeAction.current !== "save" },
    changeView: (view: SupplierRelatedView) => select(workspace.draft.record?.id ? workspace.draft.record : null, view),
    save: (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); command.mutate("save"); } };
}
