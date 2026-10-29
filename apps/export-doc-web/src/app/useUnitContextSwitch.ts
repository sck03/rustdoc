import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import type { WebSessionState } from "./webSessionStorage.ts";
import { getDefaultWorkspaceRoute } from "./productEdition.ts";
import { useConfirmUnsavedChanges } from "../ui/unsavedChangesGuard.tsx";
import { readApiError } from "../ui/formUtils.ts";
import { isCurrentSession } from "./sessionChannel.ts";

export function useUnitContextSwitch(client: ExportDocManagerApiClient, session: WebSessionState | null, accept: (next: WebSessionState, previous: WebSessionState) => void, sessionBusy = false) {
  const [busy, setBusy] = useState(false);
  const [destination, setDestination] = useState<{ route: string; token: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const current = useRef(session);
  current.current = session;
  const controller = useRef<AbortController | null>(null);
  const confirmSwitch = useConfirmUnsavedChanges();
  const cache = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  useEffect(() => {
    if (destination && (location.pathname === destination.route || session?.accessToken !== destination.token)) setDestination(null);
  }, [destination, location.pathname, session?.accessToken]);
  useEffect(() => () => controller.current?.abort(), []);
  async function switchContext(contextId: string) {
    const previous = current.current;
    if (!previous || sessionBusy || destination || controller.current || contextId === (previous.user.contextId ?? "")) return;
    const request = new AbortController();
    controller.current = request;
    setBusy(true); setError(null);
    try {
      if (!await confirmSwitch("切换单位身份") || request.signal.aborted || !isCurrentSession(current.current, previous)) return;
      await cache.cancelQueries();
      const result = await client.switchUnitContext({ body: { contextId } }, { signal: request.signal });
      if (request.signal.aborted || !isCurrentSession(current.current, previous)) return;
      const route = getDefaultWorkspaceRoute(result.user.capabilities);
      setDestination({ route, token: result.accessToken });
      cache.clear();
      accept({ ...previous, accessToken: result.accessToken, expiresAt: result.expiresAt, user: result.user }, previous);
      navigate(route, { replace: true });
    } catch (cause) {
      if (!request.signal.aborted && isCurrentSession(current.current, previous)) setError(readApiError(cause));
    } finally {
      if (controller.current === request) { controller.current = null; setBusy(false); }
    }
  }
  return { busy: busy || destination !== null, error, switchContext };
}
