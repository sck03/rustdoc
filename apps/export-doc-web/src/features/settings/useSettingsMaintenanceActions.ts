import { useMutation } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { normalizeCurrencyList } from "./settingsValueUtils.ts";
import { exchangeRateAllSupportedCurrenciesPath, exchangeRateLastCurrencyListUpdateTimePath } from "./settingsConfigurationPaths.ts";

export function useSettingsMaintenanceActions({ client, patchSettings, refetchHealth, setMessage, setSuccessMessage }: { client: ExportDocManagerApiClient; patchSettings(patches: { path: string[]; value: unknown }[]): void; refetchHealth(): Promise<unknown>; setMessage(value: string | null): void; setSuccessMessage(value: string | null): void }) {
  const cleanupMutation = useMutation({
    mutationFn: () => client.cleanupSystemLogs(),
    onSuccess: (response) => { setMessage(null); setSuccessMessage(response.message || "日志清理已完成。"); void refetchHealth(); },
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  const refreshCurrenciesMutation = useMutation({
    mutationFn: () => client.listAvailableExchangeRateCurrencies(),
    onSuccess: (response) => { const currencies = normalizeCurrencyList(response.currencies ?? []); if (!currencies.length) { setMessage("未读取到可用货币列表。"); setSuccessMessage(null); return; } patchSettings([{ path: exchangeRateAllSupportedCurrenciesPath, value: currencies }, { path: exchangeRateLastCurrencyListUpdateTimePath, value: response.fetchedAt }]); setMessage(null); setSuccessMessage(`已读取 ${currencies.length} 种可用货币，请保存设置后生效。`); },
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  return { cleanupMutation, refreshCurrenciesMutation };
}
