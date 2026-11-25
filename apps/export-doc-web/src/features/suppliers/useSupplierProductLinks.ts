import { useMemo, useRef, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useSupplierRelatedWorkspace } from "./useSupplierRelatedWorkspace.ts";
import { emptyProductLink, PRODUCT_LINK_FIELDS, productLinkRequest } from "./supplierProductLinkModel.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { requestErrorFeedback, successFeedback, warningFeedback } from "../../ui/OperationFeedback.tsx";

type Action = "save" | "deactivate" | "restore" | "delete";
export function useSupplierProductLinks(client: ExportDocManagerApiClient, supplierId: number,
  access: { edit: boolean; deactivate: boolean; delete: boolean }) {
  const workspace = useSupplierRelatedWorkspace(client, supplierId, "supplier-products", JSON.stringify(access),
    PRODUCT_LINK_FIELDS, emptyProductLink(supplierId),
    (pageNumber, pageSize, signal) => client.querySupplierProductLinks({ supplierId, pageNumber, pageSize }, { signal }));
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const optionsQuery = useQuery({ ...largeDocumentQueryOptions,
    queryKey: ["supplier-product-options", supplierId, workspace.queryScope, keyword],
    enabled: access.edit && workspace.view === "editor",
    queryFn: ({ signal }) => client.searchSupplierProductOptions({ keyword, pageNumber: 1, pageSize: 100 }, { signal }) });
  const selected = workspace.draft.record;
  const options = useMemo(() => {
    const rows = optionsQuery.data?.items ?? [];
    if (!selected?.id || rows.some(row => row.id === selected.productId)) return rows;
    return [{ id: selected.productId, productCode: selected.productCode, nameCN: selected.productNameCN,
      nameEN: selected.productNameEN }, ...rows];
  }, [optionsQuery.data, selected]);
  const confirmAction = useConfirmation();
  const activeAction = useRef<Action>("save");
  const command = useScopedMutation<boolean, Action>(workspace.scope, workspace.owner, {
    mutationFn: async (action, signal, { checkCurrent, continueInScope }) => {
      activeAction.current = action;
      const record = workspace.draft.record!;
      if (action === "save" ? !access.edit : !record.id || !(action === "delete" ? access.delete : access.deactivate)) return false;
      if (action !== "save") {
        const label = action === "delete" ? "删除供货关系" : action === "restore" ? "恢复供货关系" : "停用供货关系";
        if (!await confirmAction({ title: label, description: `确定${label}“${record.productCode || record.productNameCN}”吗？`,
          confirmLabel: label, tone: action === "restore" ? undefined : "danger" })) return false;
        checkCurrent();
        if (!await workspace.discard(label)) return false;
        checkCurrent();
      }
      const submitted = { ...workspace.draft.values };
      if (action === "delete") {
        await client.deleteSupplierProductLink({ supplierId, id: record.id, expectedVersion: record.versionNumber }, { signal });
        checkCurrent(); continueInScope(workspace.scopeFor(0, "directory")); workspace.acceptDeleted();
      } else {
        const request = { supplierId, id: record.id, body: { expectedVersion: record.versionNumber } };
        const saved = action === "restore" ? await client.restoreSupplierProductLink(request, { signal })
          : action === "deactivate" ? await client.deactivateSupplierProductLink(request, { signal })
          : record.id ? await client.updateSupplierProductLink({ supplierId, id: record.id, body: productLinkRequest(record, submitted) }, { signal })
          : await client.createSupplierProductLink({ supplierId, body: productLinkRequest(record, submitted) }, { signal });
        checkCurrent();
        // Directory cancellation occurs before accepted draft/version publication.
        continueInScope(workspace.scopeFor(saved.id, "editor"));
        await workspace.acceptSaved(saved, submitted, checkCurrent);
      }
      workspace.setFeedback(successFeedback(action === "delete" ? "供货关系已删除。" : action === "restore" ? "供货关系已恢复。" : action === "deactivate" ? "供货关系已停用。" : "供货关系已保存。"));
      return true;
    },
    refreshKeys: changed => changed ? workspace.refreshKeys : [],
    onRefreshError: error => workspace.setFeedback(warningFeedback(`操作已完成，目录刷新失败：${readApiError(error)}`)),
    onError: error => workspace.setFeedback(requestErrorFeedback(error)),
  });
  return { ...workspace, options, optionsQuery, keywordInput, setKeywordInput,
    search: () => setKeyword(keywordInput.trim()),
    command: { ...command, isEditingLocked: command.isPending && activeAction.current !== "save" },
    save: (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); command.mutate("save"); },
    select: (...args: Parameters<typeof workspace.select>) => command.isPending ? Promise.resolve() : workspace.select(...args) };
}
