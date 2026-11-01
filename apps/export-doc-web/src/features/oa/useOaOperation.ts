import { useOfficeOperation } from "../office/useOfficeData.ts";
import type { OaKind } from "./oaModel.ts";
import { officeWorkflowQueryRoots } from "../../api/queryKeys.ts";

/** Only the changed OA kind, its hub queues, notifications and worklist refresh. */
export function useOaOperation(kind: OaKind) {
  return useOfficeOperation(officeWorkflowQueryRoots(kind), false);
}
