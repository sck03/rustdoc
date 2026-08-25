import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import type { ApiSettingsResponse, AppSettings, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { cloneSettings, setNestedValue } from "../settings/settingsValueUtils.ts";

export function useReportExportDefaults({
  client,
  response,
  refetch,
  onFeedback,
}: {
  client: ExportDocManagerApiClient;
  response: ApiSettingsResponse | undefined;
  refetch: () => Promise<unknown>;
  onFeedback: (message: string, type: "success" | "error") => void;
}) {
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [isDirty, setIsDirty] = useState(false);

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
      onFeedback("导出默认设置已保存。", "success");
      await refetch();
    },
    onError: (error) => onFeedback(readApiError(error), "error"),
  });

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
    isBusy: saveMutation.isPending,
    onChange: change,
    onSave: () => {
      if (isDirty && !saveMutation.isPending) saveMutation.mutate();
    },
  };
}
