import { useMutation } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function useSettingsMaintenanceActions({ client, refetchHealth, setMessage, setSuccessMessage }: { client: ExportDocManagerApiClient; refetchHealth(): Promise<unknown>; setMessage(value: string | null): void; setSuccessMessage(value: string | null): void }) {
  const cleanupMutation = useMutation({
    mutationFn: () => client.cleanupSystemLogs(),
    onSuccess: (response) => { setMessage(null); setSuccessMessage(response.message || "日志清理已完成。"); void refetchHealth(); },
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  return { cleanupMutation };
}
