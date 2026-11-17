import type { ApiContainerPackingAnalyzeRequest, ApiContainerPackingAnalyzeResponse } from "../../../api/index.ts";
import type { ContainerPackingRulesFormState } from "./containerPackingModel.ts";

/** The request and its result travel together through preview, totals and PDF export. */
export type ContainerPackingSnapshot = {
  request: ApiContainerPackingAnalyzeRequest;
  response: ApiContainerPackingAnalyzeResponse;
  containerType: string;
  rules: ContainerPackingRulesFormState;
};
