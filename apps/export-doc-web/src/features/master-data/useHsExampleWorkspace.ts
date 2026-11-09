import { useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient, HsCodeDeclarationExample, HsCodeExampleInput } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { mergeSavedDraftFields } from "../../ui/recordDraftMerge.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

const fields = ["rawReportedHsCode", "resolvedCurrentHsCode", "productName", "specification", "source", "sourceYear", "resolutionStatus", "isManuallyVerified"] as const;
type Feedback = { tone: "success" | "error" | "warning"; message: string };
type Editor = { draft: HsCodeExampleInput; baseline: HsCodeExampleInput };
export type ExampleVersion = { id: number; expectedVersion: number };

function fromRecord(row: HsCodeDeclarationExample): HsCodeExampleInput {
  return { id: row.id, expectedVersion: row.versionNumber, rawReportedHsCode: row.rawReportedHsCode,
    resolvedCurrentHsCode: row.resolvedCurrentHsCode ?? "", productName: row.productName,
    specification: row.specification ?? "", source: row.source, sourceYear: row.sourceYear,
    resolutionStatus: row.resolutionStatus, isManuallyVerified: row.isManuallyVerified };
}

export function useHsExampleWorkspace(client: ExportDocManagerApiClient, businessYear: number, canOperate: boolean) {
  const queryClient = useQueryClient();
  const confirmation = useConfirmation();
  const [editor, setEditor] = useState<Editor | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const latest = useRef(editor); latest.current = editor;
  const operationKind = useRef<"save" | "navigate">("navigate");
  const saveScope = useScopedSave(String(canOperate), client);
  const dirty = !!editor && fields.some(key => !Object.is(editor.draft[key], editor.baseline[key]));
  const guard = useUnsavedChangesGuard({ isDirty: dirty || saveScope.isSaving });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] });

  async function run(action: Parameters<typeof saveScope.run>[0], kind: "save" | "navigate" = "navigate") {
    try { await saveScope.run(context => { operationKind.current = kind; return action(context); }); }
    catch (error) { if (!isAbortError(error)) setFeedback({ tone: "error", message: readApiError(error) }); }
  }
  function edit(row?: HsCodeDeclarationExample | null) {
    void run(async ({ checkCurrent }) => {
      if (!await guard.confirmDiscardChanges(row === null ? "关闭编辑" : "切换实例")) return;
      checkCurrent();
      const value: HsCodeExampleInput | null = row === null ? null : row ? fromRecord(row) : {
        id: 0, expectedVersion: 0, rawReportedHsCode: "", resolvedCurrentHsCode: "", productName: "",
        specification: "", source: "Manual", sourceYear: businessYear, resolutionStatus: "Unresolved", isManuallyVerified: true,
      };
      setEditor(value ? { draft: value, baseline: value } : null);
      setFeedback(null);
    });
  }
  function save() {
    if (!editor || !canOperate) return;
    const submitted = editor.draft;
    void run(async ({ signal, checkCurrent }) => {
      const response = await client.saveHsCodeKnowledgeExample({ body: submitted }, { signal });
      checkCurrent();
      const saved = fromRecord(response);
      setEditor(current => current ? { baseline: saved, draft: mergeSavedDraftFields(current.draft, submitted, saved, fields) } : null);
      setFeedback({ tone: "success", message: "申报实例已保存。" });
      try { await refresh(); }
      catch (error) { checkCurrent(); setFeedback({ tone: "warning", message: "实例已保存，列表刷新失败：" + readApiError(error) }); }
    }, "save");
  }
  async function remove(items: ExampleVersion[]) {
    if (!canOperate || !items.length) return false;
    let removed = false;
    await run(async ({ signal, checkCurrent }) => {
      const editingSelected = items.some(item => item.id === latest.current?.draft.id);
      if (editingSelected && !await guard.confirmDiscardChanges("删除当前实例")) return;
      checkCurrent();
      if (!await confirmation({ title: "删除申报实例", description: `确定删除选中的 ${items.length} 条申报实例吗？`, confirmLabel: "确认删除", tone: "danger" })) return;
      checkCurrent();
      if (items.length === 1) await client.deleteHsCodeKnowledgeExample(items[0], { signal });
      else await client.deleteHsCodeKnowledgeExamplesBatch({ body: { items } }, { signal });
      checkCurrent();
      if (editingSelected) setEditor(null);
      removed = true;
      setFeedback({ tone: "success", message: "申报实例已删除。" });
      try { await refresh(); }
      catch (error) { checkCurrent(); setFeedback({ tone: "warning", message: "实例已删除，列表刷新失败：" + readApiError(error) }); }
    });
    return removed;
  }
  return { draft: editor?.draft, feedback, busy: saveScope.isSaving, fieldsLocked: !canOperate || (saveScope.isSaving && operationKind.current !== "save"), edit, save, remove,
    change: (draft: HsCodeExampleInput) => setEditor(current => current ? { ...current, draft } : null) };
}
