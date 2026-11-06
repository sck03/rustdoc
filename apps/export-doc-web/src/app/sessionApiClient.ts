import { createExportDocManagerApiClient, type ApiClientOptions } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";

/** Authentication failures belong to the identity that sent the HTTP request. */
export function createSessionApiClient(options: ApiClientOptions, isCurrent: (requestToken?: string) => boolean) {
  return createExportDocManagerApiClient({
    ...options,
    onAuthenticationFailure: (error, requestToken) => { if (isCurrent(requestToken)) notifyAuthenticationFailure(error); },
  });
}
