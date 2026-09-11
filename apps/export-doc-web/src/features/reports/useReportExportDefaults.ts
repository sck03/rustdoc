import { useEffect, useState } from "react";
import { useMutation, type QueryObserverResult } from "@tanstack/react-query";
import type { ApiSettingsResponse, AppSettings, ExportDocManagerApiClient } from "../../api/index.ts";
import { isConcurrencyConflict, readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { cloneSettings, setNestedValue } from "../settings/settingsValueUtils.ts";

export function useReportExportDefaults({
  client,
  response,
  refetch,
  onFeedback,
}: {
  client: ExportDocManagerApiClient;
  response: ApiSettingsResponse | undefined;
  refetch: () => Promise<QueryObserverResult<ApiSettingsResponse, Error>>;
  onFeedback: (message: string, type: "success" | "error") => void;
}) {
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const requestConfirmation = useConfirmation();

  useEffect(() => {
    if (!response || isDirty) return;
    setSettings(response.settings);
  }, [isDirty, response]);

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!settings) throw new Error("导出默认设置尚未加载。");
      return client.updateSettings({ body: { settings, updateSecrets: false } });
    },
    onSuccess: async (saved) => {
      setSettings(saved.settings);
      setIsDirty(false);
      setConcurrencyMessage(null);
      onFeedback("导出默认设置已保存。", "success");
      await refetch();
    },
    onError: (error) => {
      const message = readApiError(error), conflict = isConcurrencyConflict(error);
      setConcurrencyMessage(conflict ? message : null);
      onFeedback(conflict ? "" : message, "error");
    },
  });

  async function reloadSettings() {
    if (saveMutation.isPending || !await requestConfirmation({
      title: "重新加载导出默认设置",
      description: "加载最新设置会放弃当前未保存修改，是否继续？",
      confirmLabel: "加载最新版本",
    })) return;
    const result = await refetch();
    if (result.isError) { onFeedback(readApiError(result.error), "error"); return; }
    if (!result.data) return;
    setSettings(result.data.settings);
    setIsDirty(false);
    setConcurrencyMessage(null);
    onFeedback("已加载最新导出默认设置，请检查后继续编辑。", "success");
  }

  function change(path: string[], value: unknown) {
    setSettings((current) => {
      if (!current) return current;
      const next = cloneSettings(current as unknown as Record<string, unknown>) as unknown as AppSettings;
      setNestedValue(next as unknown as Record<string, unknown>, path, value);
      return next;
    });
    setIsDirty(true);
  }

  return {
    settings,
    isDirty,
    concurrencyMessage,
    onReload: () => { void reloadSettings(); },
    isBusy: saveMutation.isPending,
    onChange: change,
    onSave: () => {
      if (isDirty && !saveMutation.isPending) saveMutation.mutate();
    },
  };
}
