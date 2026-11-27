import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { createExportDocManagerApiClient, type MaintenanceRestore } from "../../api/index.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { queryPollingInterval } from "../../api/queryPollingPolicy.ts";
import { readApiError } from "../../ui/formUtils.ts";
export function useRecoveryMaintenance() {
  const client = useMemo(() => createExportDocManagerApiClient({ baseUrl: window.location.origin }), []);
  const [csrf, setCsrf] = useState("");
  const [error, setError] = useState("");
  const command=useScopedSave("recovery-maintenance",client);
  const pending=command.isSaving;
  const [ready, setReady] = useState(false);
  const [request, setRequest] = useState<MaintenanceRestore>({ pointId: "", fileMode: "Local", targetLocation: "" });
  const init = { credentials: "same-origin" as const, headers: { "X-Maintenance-Csrf": csrf } };
  const status = useQuery({ queryKey: ["recovery-maintenance", csrf], enabled: Boolean(csrf), queryFn: ({ signal }) => client.getMaintenanceStatus({ ...init, signal }), refetchInterval: query => ["Completed", "Abandoned"].includes(query.state.data?.state ?? "") ? false : queryPollingInterval(query,2000), refetchIntervalInBackground: false, retry: false });
  const active = pending || ["Preparing", "Preflight", "ApplyingFiles", "Committing"].includes(status.data?.state ?? "");
  const call = async (action: (signal:AbortSignal) => Promise<unknown>, refresh = true) => {
    let accepted=false;
    try { await command.run(async({signal,checkCurrent})=>{setError("");await action(signal);checkCurrent();accepted=true;if(refresh)void status.refetch();}); }
    catch(cause){if(!isAbortError(cause))setError(readApiError(cause));}
    return accepted;
  };
  const login = (token: string) => call(async signal => { const session = await client.openMaintenanceSession({ body: { token } }, { credentials: "same-origin", signal }); setCsrf(session.csrfToken); }, false);
  const update = <K extends keyof MaintenanceRestore>(key: K, value: MaintenanceRestore[K]) => { setReady(false); setRequest(current => ({ ...current, [key]: value })); };
  const preflight = () => call(async signal => { await client.preflightRecoveryPoint({ body: request }, { ...init, signal }); setReady(true); setRequest(current => ({ ...current, secretAccessKey: "", sessionToken: "" })); });
  const apply = () => call(signal => client.applyRecoveryPoint({ body: { planId: status.data!.planId } }, { ...init, signal }));
  return { client, csrf, init, error, pending, active, ready, request, status, call, login, update, preflight, apply };
}
