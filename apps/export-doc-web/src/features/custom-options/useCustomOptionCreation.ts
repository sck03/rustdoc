import { useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import type { CustomOptionMap } from "./customOptionModel.ts";

export function useCustomOptionCreation(client: ExportDocManagerApiClient, optionType: string, options: string[], maxLength: number, onSelect: (value: string) => void) {
  const queryClient = useQueryClient();
  const run = useAbortableOperation();
  const pending = useRef(false);
  const [isAdding, setIsAdding] = useState(false);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  function open() {
    setDraft("");
    setError(null);
    setMessage(null);
    setIsAdding(true);
  }

  function close() {
    if (pending.current) return;
    setIsAdding(false);
    setError(null);
  }

  async function save() {
    if (pending.current) return;
    const value = draft.trim().normalize("NFC");
    if (!value || value.length > maxLength) {
      setError(`请填写 1—${maxLength} 字的选项名称。`);
      return;
    }
    const existing = options.find((option) => option.trim().toLowerCase() === value.toLowerCase());
    if (existing) {
      onSelect(existing);
      setMessage(`已选择已有选项“${existing}”。`);
      close();
      return;
    }
    pending.current = true;
    setBusy(true);
    setError(null);
    try {
      await run(async (signal) => {
        const response = await client.saveCustomOption({ optionType, body: { value } }, { signal, timeoutMs: 30_000 });
        if (signal.aborted) return;
        queryClient.setQueriesData<CustomOptionMap>({ queryKey: ["custom-options", "group"] }, (current) =>
          current && optionType in current ? { ...current, [optionType]: response.options } : current);
        void queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
        onSelect(response.options.find((option) => option.toLowerCase() === value.toLowerCase()) ?? value);
        setMessage(`已新增“${value}”，下次可直接选择。`);
        setIsAdding(false);
      });
    } catch (cause) {
      if (!isAbortError(cause)) setError(readApiError(cause));
    } finally {
      pending.current = false;
      setBusy(false);
    }
  }

  return { isAdding, draft, setDraft, busy, error, message, open, close, save };
}
