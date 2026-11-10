import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function useSettingsMaintenanceActions({ client, setMessage, setSuccessMessage }: { client: ExportDocManagerApiClient; setMessage(value: string | null): void; setSuccessMessage(value: string | null): void }) {
  const cleanupMutation = useScopedMutation("settings", client, {
    mutationFn: (_: void, signal) => client.cleanupSystemLogs({ signal }),
    onSuccess: (response) => { setMessage(null); setSuccessMessage(response.message || "日志清理已完成。"); },
    refreshKeys: () => [queryKeys.health()],
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  return { cleanupMutation };
}
