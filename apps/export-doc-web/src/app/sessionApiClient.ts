import { ApiError, createExportDocManagerApiClient, type ApiClientOptions } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";
import type { WebSessionState } from "./webSessionStorage.ts";

/** Token renewal and business-date refresh preserve the workspace; authorization changes do not. */
export function sessionAuthorizationKey(session: WebSessionState | null) {
  if (!session) return "";
  const user = session.user;
  return JSON.stringify([session.apiBaseUrl, user.id, user.contextId, user.companyScope,
    user.departmentId, user.effectiveDepartmentIds, user.role, user.isActive,
    user.employee?.id, user.employee?.appointmentId, user.employee?.status,
    user.permissionSchemeVersion, user.capabilities]);
}

/** Authentication failures belong to the identity that sent the HTTP request. */
export function createSessionApiClient(options: ApiClientOptions, isCurrent: (requestToken?: string) => boolean, isCurrentScope: () => boolean = () => true) {
  return createExportDocManagerApiClient({
    ...options,
    checkCurrent: () => {
      if (!isCurrentScope()) throw new DOMException("权限或单位身份已变化，请重新操作。", "AbortError");
      options.checkCurrent?.();
    },
    onAuthenticationFailure: (error, requestToken) => { if (isCurrentScope() && isCurrent(requestToken)) notifyAuthenticationFailure(error); },
  });
}

/** Revocation owns the submitted credentials, independent of the live workspace. */
export async function revokeSession(options: ApiClientOptions & { accessToken: string }) {
  try {
    await createExportDocManagerApiClient(options).logout();
  } catch (error) {
    // An already expired/revoked session needs no further server-side cleanup.
    if (!(error instanceof ApiError && error.status === 401)) throw error;
  }
}
