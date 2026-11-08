import { useLayoutEffect, useState } from "react";
import type { ApiSingleWindowLockedFieldDto } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import type { SingleWindowDocumentWorkspace } from "./useSingleWindowDocumentWorkspace.ts";

type Options<T extends object> = {
  workspace: SingleWindowDocumentWorkspace<T>;
  document: T | null;
  hasUnsavedChanges: boolean;
  loadLockedFields(signal: AbortSignal): Promise<{ fields: ApiSingleWindowLockedFieldDto[] }>;
  unlockFields(fieldKeys: string[], signal: AbortSignal): Promise<{ document: T; lockedFields: ApiSingleWindowLockedFieldDto[] }>;
};

export function useSingleWindowLockedFields<T extends object>({ workspace, document, hasUnsavedChanges, loadLockedFields, unlockFields }: Options<T>) {
  const requestConfirmation = useConfirmation();
  const [isOpen, setIsOpen] = useState(false);
  const [fields, setFields] = useState<ApiSingleWindowLockedFieldDto[]>([]);
  const [selectedFieldKeys, setSelectedFieldKeys] = useState<Set<string>>(() => new Set());
  useLayoutEffect(() => {
    setIsOpen(false); setFields([]); setSelectedFieldKeys(new Set());
  }, [workspace.scope, workspace.owner, workspace.enabled]);

  function toggleField(key: string) {
    setSelectedFieldKeys(current => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }
  return {
    isOpen, fields, selectedFieldKeys,
    isPending: workspace.isPending,
    open: () => workspace.run(async context => {
      if (!document) return;
      if (hasUnsavedChanges) {
        const accepted = await requestConfirmation({ title: "保存后查看锁定字段", description: "当前草稿有未保存修改，需要先保存后再读取锁定字段。", confirmLabel: "保存并继续" });
        context.checkCurrent();
        if (!accepted) return;
        await workspace.persist(context);
      }
      const result = await loadLockedFields(context.signal);
      context.checkCurrent();
      setFields(result.fields); setSelectedFieldKeys(new Set()); setIsOpen(true);
    }),
    close: () => setIsOpen(false),
    toggleField,
    toggleAll: () => setSelectedFieldKeys(current => current.size === fields.length ? new Set() : new Set(fields.map(field => field.key))),
    unlockSelected: () => workspace.run(async context => {
      if (!document || selectedFieldKeys.size === 0) return;
      const result = await unlockFields(Array.from(selectedFieldKeys), context.signal);
      context.checkCurrent();
      workspace.accept(document, result.document);
      setFields(result.lockedFields); setSelectedFieldKeys(new Set());
      await workspace.refresh(context);
    }),
  };
}
