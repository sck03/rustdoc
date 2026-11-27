import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiRepositoryMaintenanceRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useTrackedJob } from "../jobs/useTrackedJob.ts";

const affected = [["recovery-repository-maintenance"], queryKeys.backupPlan(), queryKeys.recoveryArchives()] as const;
export function useRecoveryRepository(client: ExportDocManagerApiClient) {
  const [error, setError] = useState("");
  const command = useScopedSave("recovery-repository", client);
  const tracking = useTrackedJob(client, true, affected);
  useEffect(() => { setError(""); }, [client]);
  const status = useQuery({ queryKey: affected[0], queryFn: ({ signal }) => client.getRecoveryRepositoryMaintenance({ signal }) });
  const start = async (body: ApiRepositoryMaintenanceRequest) => {
    let accepted = false;
    try {
      await command.run(async ({ signal, checkCurrent }) => {
        setError("");
        const result = await client.maintainRecoveryRepository({ body }, { signal });
        checkCurrent();
        tracking.track(result.jobId);
        accepted = true;
      });
    } catch (cause) { if (!isAbortError(cause)) setError(readApiError(cause)); }
    return accepted;
  };
  return { status, tracking, start, busy: command.isSaving || tracking.active,
    error: error || (status.isError ? readApiError(status.error) : tracking.job.isError ? readApiError(tracking.job.error) : "") };
}
