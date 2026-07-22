import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import type { ApiSingleWindowLockedFieldDto } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";

type LockedFieldsResponse = { fields: ApiSingleWindowLockedFieldDto[] };
type UnlockResponse<TDocument> = {
  document: TDocument;
  lockedFields: ApiSingleWindowLockedFieldDto[];
  message?: string;
};
type Options<TDocument> = {
  document: TDocument | null;
  isDocumentValid: boolean;
  hasUnsavedChanges: boolean;
  saveDocument(): Promise<TDocument>;
  loadLockedFields(): Promise<LockedFieldsResponse>;
  unlockFields(fieldKeys: string[]): Promise<UnlockResponse<TDocument>>;
  applyPersistedDocument(document: TDocument): void;
  clearMessages(): void;
  showError(message: string): void;
  showSuccess(message: string): void;
};

export function useSingleWindowLockedFields<TDocument>({
  document,
  isDocumentValid,
  hasUnsavedChanges,
  saveDocument,
  loadLockedFields,
  unlockFields,
  applyPersistedDocument,
  clearMessages,
  showError,
  showSuccess,
}: Options<TDocument>) {
  const requestConfirmation = useConfirmation();
  const [isOpen, setIsOpen] = useState(false);
  const [fields, setFields] = useState<ApiSingleWindowLockedFieldDto[]>([]);
  const [selectedFieldKeys, setSelectedFieldKeys] = useState<Set<string>>(() => new Set());

  const loadMutation = useMutation({
    mutationFn: async () => {
      if (!document || !isDocumentValid) return null;
      let savedDocument: TDocument | null = null;
      if (hasUnsavedChanges) {
        const confirmed = await requestConfirmation({
          title: "保存后查看锁定字段",
          description: "当前草稿有未保存修改，需要先保存后再读取锁定字段。",
          confirmLabel: "保存并继续",
        });
        if (!confirmed) return null;
        savedDocument = await saveDocument();
      }
      return { savedDocument, response: await loadLockedFields() };
    },
    onSuccess: (result) => {
      if (!result) return;
      if (result.savedDocument) applyPersistedDocument(result.savedDocument);
      setFields(result.response.fields);
      setSelectedFieldKeys(new Set());
      setIsOpen(true);
      clearMessages();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const unlockMutation = useMutation({
    mutationFn: unlockFields,
    onSuccess: (response) => {
      applyPersistedDocument(response.document);
      setFields(response.lockedFields);
      setSelectedFieldKeys(new Set());
      showSuccess(response.message || "字段已恢复为当前建议值。");
    },
    onError: (error) => showError(readApiError(error)),
  });

  function toggleField(key: string) {
    setSelectedFieldKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function toggleAll() {
    setSelectedFieldKeys((current) => current.size === fields.length
      ? new Set()
      : new Set(fields.map((field) => field.key)));
  }

  function unlockSelected() {
    const fieldKeys = Array.from(selectedFieldKeys);
    if (fieldKeys.length > 0 && isDocumentValid) unlockMutation.mutate(fieldKeys);
  }

  return {
    isOpen,
    fields,
    selectedFieldKeys,
    isPending: loadMutation.isPending || unlockMutation.isPending,
    open: () => {
      if (!document || !isDocumentValid) return;
      clearMessages();
      loadMutation.mutate();
    },
    close: () => setIsOpen(false),
    toggleField,
    toggleAll,
    unlockSelected,
  };
}

export type SingleWindowLockedFieldsWorkspace = ReturnType<typeof useSingleWindowLockedFields>;
