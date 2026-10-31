import { useOfficeOperation } from "../office/useOfficeData.ts";
import type { OaKind } from "./oaModel.ts";
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";

/** Only the changed OA kind, its hub queues, notifications and worklist refresh. */
export function useOaOperation(kind: OaKind) {
  return useOfficeOperation([["office", "oa", kind], ["office", "oa", "hub"], ["office", "communication"],
    ["office", "people", "clearance"], queryKeys.unitWorkflowTasks(), queryKeys.unitWorkflowSummary(),
    ...(kind === "personnel" ? [["office", "people"], ...identityQueryRoots] : [])], false);
}
