import { useEffect } from "react";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";
import { calculateBusinessDateRefreshDelay } from "../api/businessDateRefreshModel.ts";
import { queryPollingInterval } from "../api/queryPollingPolicy.ts";
import type { WebSessionState } from "./webSessionStorage.ts";

type BusinessDateSessionRefreshOptions = {
  client: ExportDocManagerApiClient;
  desktopContextLoading: boolean;
  session: WebSessionState | null;
  sessionRef: { current: WebSessionState | null };
  setSession: (session: WebSessionState) => void;
  validatedUserTokenRef: { current: string | undefined };
  allowAuthenticationFailure?: () => boolean;
};

export function useBusinessDateSessionRefresh({
  client,
  desktopContextLoading,
  session,
  sessionRef,
  setSession,
  validatedUserTokenRef,
  allowAuthenticationFailure,
}: BusinessDateSessionRefreshOptions) {
  const accessToken = session?.accessToken;
  const validUntil = session?.user.businessDateValidUntilUtc;
  useEffect(() => {
    if (!accessToken || desktopContextLoading) return;
    let disposed = false;
    let timerId: number | undefined;
    let active: AbortController | undefined;
    let failures = 0;
    const clearTimer = () => window.clearTimeout(timerId);
    const schedule = (delay: number | false | null) => {
      clearTimer();
      if (!disposed && typeof delay === "number") timerId = window.setTimeout(() => void refresh(), delay);
    };
    async function refresh() {
      if (disposed || active || document.visibilityState === "hidden") return;
      clearTimer();
      const controller = new AbortController();
      active = controller;
      try {
        const user = await client.getCurrentUser({ signal: controller.signal });
        const currentSession = sessionRef.current;
        if (disposed || controller.signal.aborted || !currentSession || currentSession.accessToken !== accessToken) return;
        failures = 0;
        validatedUserTokenRef.current = accessToken;
        if (JSON.stringify(currentSession.user) !== JSON.stringify(user)) setSession({ ...currentSession, user });
        const delay = calculateBusinessDateRefreshDelay(user.businessDateValidUntilUtc);
        // A skewed clock or stale server deadline must not create a zero-delay loop.
        schedule(delay === 0 ? queryPollingInterval({ state: { error: null, fetchFailureCount: 0 } }) : delay);
      } catch (error) {
        if (disposed || controller.signal.aborted || sessionRef.current?.accessToken !== accessToken) return;
        if (allowAuthenticationFailure?.() ?? true) notifyAuthenticationFailure(error);
        schedule(queryPollingInterval({ state: { error, fetchFailureCount: ++failures } }, false));
      } finally {
        if (active === controller) active = undefined;
      }
    }
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") void refresh();
    };
    const refreshWhenOnline = () => void refresh();
    document.addEventListener("visibilitychange", refreshWhenVisible);
    window.addEventListener("online", refreshWhenOnline);
    if (validatedUserTokenRef.current !== accessToken) void refresh();
    else schedule(calculateBusinessDateRefreshDelay(validUntil));
    return () => {
      disposed = true;
      clearTimer();
      active?.abort();
      document.removeEventListener("visibilitychange", refreshWhenVisible);
      window.removeEventListener("online", refreshWhenOnline);
    };
  }, [accessToken, client, desktopContextLoading, validUntil, sessionRef, setSession, validatedUserTokenRef, allowAuthenticationFailure]);
}
