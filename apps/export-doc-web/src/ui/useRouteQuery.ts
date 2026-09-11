import { useCallback, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { patchRouteQuery, type RouteQueryPatch } from "./routeQueryState.ts";

export function useRouteQuery() {
  const location = useLocation();
  const navigate = useNavigate();
  const current = useRef({ locationKey: location.key, search: location.search });
  // Local draft renders can occur before the router commits a navigation.
  // Only a new history entry may replace the pending query, otherwise a
  // following view change could lose the object ID written just before it.
  if (current.current.locationKey !== location.key) current.current = { locationKey: location.key, search: location.search };
  const update = useCallback((patch: RouteQueryPatch, replace = true) => {
    const search = patchRouteQuery(current.current.search, patch);
    current.current.search = search;
    navigate({ pathname: location.pathname, search, hash: location.hash }, { replace, state: location.state });
  }, [location.hash, location.pathname, location.state, navigate]);
  return { params: new URLSearchParams(location.search), update };
}
