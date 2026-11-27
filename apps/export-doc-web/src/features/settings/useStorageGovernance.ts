import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useTrackedJob } from "../jobs/useTrackedJob.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
const affected = [["storage-governance"], queryKeys.fileStorage(), queryKeys.fileStoragePolicy()] as const;
export function useStorageGovernance(client: ExportDocManagerApiClient, enabled: boolean) {
  const cache = useQueryClient();
  const [after, setAfter] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const command = useScopedSave(`storage-governance:${enabled}`, client);
  useEffect(() => { setAfter(""); setError(""); setMessage(""); }, [client, enabled]);
  const capacity = useQuery({ queryKey: ["storage-governance", "capacity"], queryFn: ({ signal }) => client.getStorageCapacity({ signal }), enabled });
  const retention = useQuery({ queryKey: ["storage-governance", "retention", after], queryFn: ({ signal }) => client.queryFileRetention({ body: { after } }, { signal }), enabled });
  const locations = useQuery({ queryKey: queryKeys.fileStorage(), queryFn: ({ signal }) => client.getFileStorage({ signal }), enabled });
  const tracking = useTrackedJob(client, enabled, affected);
  const { job, active } = tracking;
  const run = async (action: (signal: AbortSignal) => Promise<unknown>) => {
    let accepted = false;
    try {
      await command.run(async ({ signal, checkCurrent }) => {
        setError(""); setMessage("");
        const result = await action(signal); checkCurrent(); accepted = true;
        if (result && typeof result === "object" && "jobId" in result) tracking.track(String(result.jobId));
        if (result && typeof result === "object" && "message" in result) setMessage(String(result.message));
        void cache.invalidateQueries({ queryKey: ["storage-governance"] });
      });
    } catch (cause) { if (!isAbortError(cause)) setError(readApiError(cause)); }
    return accepted;
  };
  return { capacity, retention, locations, tracking, job:job.data, after, setAfter, error: error || (job.isError ? readApiError(job.error) : locations.isError ? readApiError(locations.error) : capacity.isError ? readApiError(capacity.error) : retention.isError ? readApiError(retention.error) : ""), message, busy: command.isSaving || active, run };
}
