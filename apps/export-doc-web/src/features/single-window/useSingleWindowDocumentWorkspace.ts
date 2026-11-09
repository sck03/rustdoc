import { useRef, useState, type Dispatch, type SetStateAction } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";
import type { ApiSingleWindowRepairGroupsResponse } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { mergeSavedDraftFields } from "../../ui/recordDraftMerge.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";

type Context = Parameters<Parameters<ReturnType<typeof useScopedSave>["run"]>[0]>[0];
type Change<T> = { document: T; changedCount: number };
type ScopedClear<T> = { snapshot: T; groupKey: string; categoryKey?: string; categoryLabel?: string };
type Setter<T> = Dispatch<SetStateAction<T>>;
type Options<T extends object> = {
  scope: string; owner: unknown; enabled: boolean; document: T | null;
  setDocument: Setter<T | null>; setUndoDocument: Setter<T | null>;
  setMessage: Setter<string | null>; setSuccessMessage: Setter<string | null>;
  setPersistedDocumentSnapshot: Setter<string | null>;
  documentQueryKey: QueryKey; reviewQueryKey: QueryKey;
  snapshot(document: T): string;
  saveDocument(document: T, signal: AbortSignal): Promise<{ document: T; message?: string }>;
  getDocument(signal: AbortSignal): Promise<T>;
  buildDefaults(signal: AbortSignal): Promise<T>;
  repair(groupKeys: string[], signal: AbortSignal): Promise<ApiSingleWindowRepairGroupsResponse>;
  fillEmpty(document: T, defaults: T): Change<T>;
  clearScope(document: T, defaults: T, groupKey: string, categoryKey: string): Change<T>;
  clearManual(document: T): Change<T>;
  clearMessage(request: ScopedClear<T>, count: number): string;
};
const serverFields = new Set(["id", "sourceId", "draftRevision", "expectedDraftRevision", "createdAt", "updatedAt"]);

/** One cancellation/serialization boundary for saves, confirmations and document tools. */
export function useSingleWindowDocumentWorkspace<T extends object>(options: Options<T>) {
  const { document, setDocument, setUndoDocument, setMessage, setSuccessMessage } = options;
  const latest = useRef(document);
  latest.current = document;
  const lock = useScopedSave(`${options.scope}:${options.enabled}`, options.owner);
  const queryClient = useQueryClient();
  const requestConfirmation = useConfirmation();
  const [acknowledged, setAcknowledged] = useState<{ scope: string; owner: unknown; document: T } | null>(null);

  function merge(current: T | null, submitted: T, saved: T) {
    return current && mergeSavedDraftFields(current, submitted, saved,
      (Object.keys(submitted) as (keyof T)[]).filter(key => !serverFields.has(String(key))));
  }
  function accept(submitted: T, saved: T) {
    setAcknowledged({ scope: options.scope, owner: options.owner, document: saved });
    latest.current = merge(latest.current, submitted, saved);
    setDocument(latest.current);
    setUndoDocument(null);
    options.setPersistedDocumentSnapshot(options.snapshot(saved));
    queryClient.setQueryData(options.documentQueryKey, saved);
  }
  async function refresh(context: Context) {
    try {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }, { throwOnError: true }),
        queryClient.invalidateQueries({ queryKey: options.reviewQueryKey }, { throwOnError: true }),
      ]);
      context.checkCurrent();
    } catch (error) {
      context.checkCurrent();
      setMessage(`草稿已保存，关联信息刷新失败：${readApiError(error)}`);
    }
  }
  async function persist(context: Context) {
    const submitted = latest.current;
    if (!submitted) throw new DOMException("草稿已关闭。", "AbortError");
    const response = await options.saveDocument(submitted, context.signal);
    context.checkCurrent();
    accept(submitted, response.document);
    setSuccessMessage(response.message || "草稿已保存。");
    return response.document;
  }
  async function run(action: (context: Context) => Promise<void>) {
    if (!options.enabled) return;
    try {
      await lock.run(async context => {
        setMessage(null); setSuccessMessage(null);
        await action(context);
      });
    } catch (error) {
      if (!isAbortError(error)) setMessage(readApiError(error));
    }
  }
  function tool(transform: (current: T, defaults: T) => Change<T>, message: (count: number) => string, confirmation?: Parameters<typeof requestConfirmation>[0]) {
    return run(async context => {
      if (confirmation) {
        const accepted = await requestConfirmation(confirmation);
        context.checkCurrent();
        if (!accepted) return;
      }
      const submitted = latest.current;
      if (!submitted) return;
      const defaults = await options.buildDefaults(context.signal);
      context.checkCurrent();
      const result = transform(submitted, defaults);
      const current = latest.current;
      setDocument(value => merge(value, submitted, result.document));
      setUndoDocument(result.changedCount > 0 ? current : null);
      setSuccessMessage(message(result.changedCount));
    });
  }
  return {
    scope: options.scope, owner: options.owner, enabled: options.enabled,
    persistedDocument: acknowledged?.scope === options.scope && acknowledged.owner === options.owner ? acknowledged.document : undefined,
    isPending: lock.isSaving, run, persist, accept, refresh,
    save: () => run(async context => { await persist(context); await refresh(context); }),
    restore: () => tool((current, defaults) => ({ document: defaults, changedCount: options.snapshot(current) === options.snapshot(defaults) ? 0 : 1 }),
      count => count ? "已恢复建议值，保存后写入草稿。" : "当前已是建议值。",
      { title: "重新套用建议值", description: "系统将按当前发票重新生成建议值。", details: ["原来的手工覆盖内容会被替换。"], confirmLabel: "重新套用" }),
    fillEmpty: () => tool(options.fillEmpty, count => count ? `已回填 ${count} 个空白项，保存后写入草稿。` : "当前可回填的空白项已补齐。"),
    clearScope: (groupKey: string, categoryKey?: string, categoryLabel?: string) => tool(
      (current, defaults) => options.clearScope(current, defaults, groupKey, categoryKey ?? ""),
      count => options.clearMessage({ snapshot: document!, groupKey, categoryKey, categoryLabel }, count),
      { title: "恢复建议值", description: `确定恢复“${categoryLabel || groupKey}”的当前发票建议值吗？`, confirmLabel: "确认恢复" }),
    clearManual: () => run(async context => {
      const accepted = await requestConfirmation({ title: "清空手工覆盖", description: "确定清空手工补充的覆盖字段吗？", details: ["系统回写值会保留。", "保存后才会写入草稿。"], confirmLabel: "清空覆盖" });
      context.checkCurrent();
      const current = latest.current;
      if (!accepted || !current) return;
      const result = options.clearManual(current);
      setDocument(result.document); setUndoDocument(result.changedCount ? current : null);
      setSuccessMessage(result.changedCount ? "已清空手工覆盖，保存后写入草稿。" : "当前没有可清空的手工覆盖字段。");
    }),
    repair: (groupKeys: string[], dirty: boolean) => run(async context => {
      if (groupKeys.length === 0) return;
      if (dirty) {
        const accepted = await requestConfirmation({ title: "保存并自动修复", description: "需要先保存当前草稿再执行自动修复。", confirmLabel: "保存并修复" });
        context.checkCurrent();
        if (!accepted) return;
      }
      const submitted = dirty ? await persist(context) : latest.current;
      if (!submitted) return;
      const response = await options.repair(groupKeys, context.signal);
      context.checkCurrent();
      const saved = await options.getDocument(context.signal);
      context.checkCurrent();
      accept(submitted, saved);
      queryClient.setQueryData(options.reviewQueryKey, response.review);
      setSuccessMessage(response.message || "预检分组已修复。");
      await refresh(context);
    }),
  };
}

export type SingleWindowDocumentWorkspace<T extends object> = ReturnType<typeof useSingleWindowDocumentWorkspace<T>>;
