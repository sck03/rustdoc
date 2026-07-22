import { useState } from "react";
import { useMutation, type QueryClient } from "@tanstack/react-query";
import type { ApiCustomsCooDocumentDto, ApiCustomsCooProducerProfileInputDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { applyProducerProfileToCooItem, buildProducerProfileInputFromCooItem, buildProducerProfileRowLabel, countProducerProfileChanges } from "./customsCooModel.ts";
import { cloneEditorDocument } from "./singleWindowEditorTools.ts";

type Options = {
  client: ExportDocManagerApiClient;
  queryClient: QueryClient;
  document: ApiCustomsCooDocumentDto | null;
  setDocument(next: ApiCustomsCooDocumentDto): void;
  setUndoDocument(next: ApiCustomsCooDocumentDto | null): void;
  setMessage(next: string | null): void;
  setSuccessMessage(next: string | null): void;
};

export function useCustomsCooProducerProfiles({ client, queryClient, document, setDocument, setUndoDocument, setMessage, setSuccessMessage }: Options) {
  const [rowIndex, setRowIndex] = useState<number | null>(null);
  const [savingRowIndex, setSavingRowIndex] = useState<number | null>(null);
  const saveMutation = useMutation({
    mutationFn: (request: { rowIndex: number; profile: ApiCustomsCooProducerProfileInputDto }) => client.createCustomsCooProducerProfile({ body: { profile: request.profile } }),
    onMutate: (request) => setSavingRowIndex(request.rowIndex),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "当前生产企业已保存到资料库。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowCustomsCooProducerProfilesRoot() });
    },
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
    onSettled: () => setSavingRowIndex(null),
  });
  function open(index: number) { if (!document?.items[index]) return; setRowIndex(index); setMessage(null); setSuccessMessage(null); }
  function apply(profile: ApiCustomsCooProducerProfileInputDto) {
    if (!document || rowIndex === null || !document.items[rowIndex]) return;
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
    if (!profile.ciqRegNo.trim() && !profile.prdcEtpsName.trim()) { setMessage("请先填写当前货项的生产企业代码或生产企业名称。"); setSuccessMessage(null); return; }
    setMessage(null); setSuccessMessage(null); saveMutation.mutate({ rowIndex: index, profile });
  }
  const currentProfile = rowIndex !== null && document?.items[rowIndex] ? buildProducerProfileInputFromCooItem(document.items[rowIndex], document) : null;
  const rowLabel = rowIndex !== null && document?.items[rowIndex] ? buildProducerProfileRowLabel(document.items[rowIndex], rowIndex) : "";
  return { rowIndex, savingRowIndex, isPending: saveMutation.isPending, currentProfile, rowLabel, open, apply, save, close: () => setRowIndex(null) };
}
