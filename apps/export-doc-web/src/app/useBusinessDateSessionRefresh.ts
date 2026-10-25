import { useCallback, useEffect, useRef } from "react";
import type { QueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";
import { calculateBusinessDateRefreshDelay } from "../api/businessDateRefreshModel.ts";
import type { WebSessionState } from "./webSessionStorage.ts";

type BusinessDateSessionRefreshOptions = {
  client: ExportDocManagerApiClient;
  desktopContextLoading: boolean;
  queryClient: QueryClient;
  session: WebSessionState | null;
  sessionRef: { current: WebSessionState | null };
  setSession: (session: WebSessionState) => void;
  validatedUserTokenRef: { current: string | undefined };
};

export function useBusinessDateSessionRefresh({
  client,
  desktopContextLoading,
  queryClient,
  session,
  sessionRef,
  setSession,
  validatedUserTokenRef,
}: BusinessDateSessionRefreshOptions) {
  const activeRefreshRef = useRef<{ accessToken: string; promise: Promise<void> } | null>(null);
  const accessToken = session?.accessToken;

  const refreshCurrentUser = useCallback(() => {
    if (!accessToken || desktopContextLoading) {
      return Promise.resolve();
    }
    if (activeRefreshRef.current?.accessToken === accessToken) {
      return activeRefreshRef.current.promise;
    }

    const refresh = client.getCurrentUser()
      .then((user) => {
        const currentSession = sessionRef.current;
        if (!currentSession || currentSession.accessToken !== accessToken) {
          return;
        }

        if (JSON.stringify(currentSession.user.capabilities) !== JSON.stringify(user.capabilities)) {
          queryClient.clear();
        }
        const nextSession = { ...currentSession, user };
        setSession(nextSession);
      })
      .catch((error) => {
        if (sessionRef.current?.accessToken === accessToken) notifyAuthenticationFailure(error);
      })
      .finally(() => {
        if (activeRefreshRef.current?.promise === refresh) {
          activeRefreshRef.current = null;
        }
      });
    activeRefreshRef.current = { accessToken, promise: refresh };
    return refresh;
  }, [accessToken, client, desktopContextLoading, queryClient, sessionRef, setSession]);

  useEffect(() => {
    // Login/renewal responses contain a fresh user. Restored sessions still
    // refresh once, and visibility/online/business-date events refresh below.
    if (validatedUserTokenRef.current === accessToken) return;
    void refreshCurrentUser();
  }, [accessToken, refreshCurrentUser, validatedUserTokenRef]);

  useEffect(() => {
    if (!accessToken || desktopContextLoading) {
      return undefined;
    }

    const delay = calculateBusinessDateRefreshDelay(session?.user.businessDateValidUntilUtc);
    const timerId = delay === null
      ? undefined
      : window.setTimeout(() => void refreshCurrentUser(), delay);
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") {
        void refreshCurrentUser();
      }
    };
    const refreshWhenOnline = () => void refreshCurrentUser();
    document.addEventListener("visibilitychange", refreshWhenVisible);
    window.addEventListener("online", refreshWhenOnline);

    return () => {
      if (timerId !== undefined) {
        window.clearTimeout(timerId);
      }
      document.removeEventListener("visibilitychange", refreshWhenVisible);
      window.removeEventListener("online", refreshWhenOnline);
    };
  }, [accessToken, desktopContextLoading, refreshCurrentUser, session?.user.businessDateValidUntilUtc]);
}
