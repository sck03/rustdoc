import { useCallback, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { patchRouteQuery, type RouteQueryPatch } from "./routeQueryState.ts";

export function useRouteQuery() {
  const location = useLocation();
  const navigate = useNavigate();
  const current = useRef({ location, search: location.search });
  // Local draft renders can occur before the router commits a navigation.
  // A committed location replaces the pending query. Hash edits can retain a
  // history key, while local draft renders retain the same location object.
  if (current.current.location !== location) current.current = { location, search: location.search };
  const update = useCallback((patch: RouteQueryPatch, replace = true) => {
    const search = patchRouteQuery(current.current.search, patch);
    current.current.search = search;
    navigate({ pathname: location.pathname, search, hash: location.hash }, { replace, state: location.state });
  }, [location.hash, location.pathname, location.state, navigate]);
  return { params: new URLSearchParams(location.search), update };
}
