import { useLayoutEffect, useState } from "react";
import type { QueryClient } from "@tanstack/react-query";
import type { ApiCustomsCooDocumentDto, ApiCustomsCooProducerProfileInputDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { applyProducerProfileToCooItem, buildProducerProfileInputFromCooItem, buildProducerProfileRowLabel, countProducerProfileChanges } from "./customsCooModel.ts";
import { cloneEditorDocument } from "./singleWindowEditorTools.ts";
import type { SingleWindowDocumentWorkspace } from "./useSingleWindowDocumentWorkspace.ts";

type Options = {
  workspace: SingleWindowDocumentWorkspace<ApiCustomsCooDocumentDto>;
  client: ExportDocManagerApiClient;
  queryClient: QueryClient;
  document: ApiCustomsCooDocumentDto | null;
  setDocument(next: ApiCustomsCooDocumentDto): void;
  setUndoDocument(next: ApiCustomsCooDocumentDto | null): void;
  setMessage(next: string | null): void;
  setSuccessMessage(next: string | null): void;
};

export function useCustomsCooProducerProfiles({ workspace, client, queryClient, document, setDocument, setUndoDocument, setMessage, setSuccessMessage }: Options) {
  const [rowIndex, setRowIndex] = useState<number | null>(null);
  const [savingRowIndex, setSavingRowIndex] = useState<number | null>(null);
  useLayoutEffect(() => { setRowIndex(null); setSavingRowIndex(null); }, [workspace.scope, workspace.owner, workspace.enabled]);
  function open(index: number) { if (!document?.items[index]) return; setRowIndex(index); setMessage(null); setSuccessMessage(null); }
  function apply(profile: ApiCustomsCooProducerProfileInputDto) {
    if (!workspace.enabled || !document || rowIndex === null || !document.items[rowIndex]) return;
    const snapshot = cloneEditorDocument(document);
    const current = document.items[rowIndex];
    const next = applyProducerProfileToCooItem(current, profile);
    if (countProducerProfileChanges(current, next) === 0) { setRowIndex(null); setMessage(null); setSuccessMessage("当前货项已经是这条生产企业资料。"); return; }
    setDocument({ ...document, items: document.items.map((item, index) => index === rowIndex ? next : item) });
    setUndoDocument(snapshot); setRowIndex(null); setMessage(null); setSuccessMessage(`已将生产企业资料回填到第 ${rowIndex + 1} 行，保存后写入草稿。`);
  }
  function save(index: number) {
    if (!document?.items[index]) return;
    const profile = buildProducerProfileInputFromCooItem(document.items[index], document);
    if (!profile.ciqRegNo?.trim() && !profile.prdcEtpsName?.trim()) { setMessage("请先填写当前货项的生产企业代码或生产企业名称。"); setSuccessMessage(null); return; }
    void workspace.run(async context => {
      setSavingRowIndex(index);
      try {
        const response = await client.createCustomsCooProducerProfile({ body: { profile } }, { signal: context.signal });
        context.checkCurrent();
        setSuccessMessage(response.message || "当前生产企业已保存到资料库。");
        try { await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowCustomsCooProducerProfilesRoot() }, { throwOnError: true }); }
        catch (error) { context.checkCurrent(); setMessage(`资料已保存，目录刷新失败：${readApiError(error)}`); }
      } finally { if (!context.signal.aborted) setSavingRowIndex(null); }
    });
  }
  const currentProfile = rowIndex !== null && document?.items[rowIndex] ? buildProducerProfileInputFromCooItem(document.items[rowIndex], document) : null;
  const rowLabel = rowIndex !== null && document?.items[rowIndex] ? buildProducerProfileRowLabel(document.items[rowIndex], rowIndex) : "";
  return { rowIndex, savingRowIndex, isPending: workspace.isPending && savingRowIndex !== null, currentProfile, rowLabel, open, apply, save, close: () => setRowIndex(null) };
}
