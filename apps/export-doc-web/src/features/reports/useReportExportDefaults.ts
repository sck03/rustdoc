import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ApiSettingsResponse, AppSettings, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isConcurrencyConflict, readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { useScopedConfirmation } from "../../ui/useScopedConfirmation.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { cloneSettings, setNestedValue } from "../settings/settingsValueUtils.ts";

export function useReportExportDefaults({
  client,
  response,
  onFeedback,
}: {
  client: ExportDocManagerApiClient;
  response: ApiSettingsResponse | undefined;
  onFeedback: (message: string, type: "success" | "error") => void;
}) {
  const cache = useQueryClient();
  const [draft, setDraft] = useState<{ client: ExportDocManagerApiClient; settings: AppSettings; baseline: AppSettings } | null>(null);
  const settings = draft?.client === client ? draft.settings : response?.settings ?? null;
  const isDirty = draft?.client === client && draft.settings !== draft.baseline;
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const confirmationScope = useMemo(() => ({ client, draft }), [client, draft]);
  const requestConfirmation = useScopedConfirmation(confirmationScope, useConfirmation());

  useEffect(() => {
    if (!response || isDirty) return;
    setDraft({ client, settings: response.settings, baseline: response.settings });
  }, [client, isDirty, response]);
  useEffect(() => setConcurrencyMessage(null), [client]);

  type Command = { kind: "save"; settings: AppSettings } | { kind: "reload" };
  const command = useScopedMutation("report-export-defaults", client, {
    mutationFn: async (input: Command, signal): Promise<ApiSettingsResponse> => {
      if (input.kind === "reload") return client.getSettings({ signal });
      const saved = await client.updateSettings({ body: { settings: input.settings, updateSecrets: false } }, { signal });
      return { settings: saved.settings, secrets: saved.secrets, storagePolicy: response?.storagePolicy ?? "" };
    },
    onSuccess: (saved, input) => {
      cache.setQueryData(queryKeys.settings(), saved);
      setDraft({ client, settings: saved.settings, baseline: saved.settings });
      setConcurrencyMessage(null);
      onFeedback(input.kind === "save" ? "导出默认设置已保存。" : "已加载最新导出默认设置，请检查后继续编辑。", "success");
    },
    refreshKeys: (_, input) => input.kind === "save" ? [queryKeys.settings()] : [],
    onError: (error) => {
      const message = readApiError(error), conflict = isConcurrencyConflict(error);
      // A failed read cannot resolve the earlier version conflict.
      if (conflict) setConcurrencyMessage(message);
      onFeedback(conflict ? "" : message, "error");
    },
  });

  async function reloadSettings() {
    if (command.isPending || !await requestConfirmation({
      title: "重新加载导出默认设置",
      description: "加载最新设置会放弃当前未保存修改，是否继续？",
      confirmLabel: "加载最新版本",
    })) return;
    command.mutate({ kind: "reload" });
  }

  function change(path: string[], value: unknown) {
    if (command.isPending || !settings) return;
    setDraft(current => {
      const owned = current?.client === client ? current : { client, settings, baseline: settings };
      const next = cloneSettings(owned.settings as unknown as Record<string, unknown>) as unknown as AppSettings;
      setNestedValue(next as unknown as Record<string, unknown>, path, value);
      return { ...owned, settings: next };
    });
  }

  return {
    settings,
    isDirty,
    concurrencyMessage,
    onReload: () => { void reloadSettings(); },
    isBusy: command.isPending,
    onChange: change,
    onSave: () => {
      if (isDirty && settings && !command.isPending) command.mutate({ kind: "save", settings });
    },
  };
}
