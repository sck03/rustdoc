import { lazy } from "react";
import { Route } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaKinds } from "./oaModel.ts";

const Requests = lazy(() => import("./OaRequestsPage.tsx").then((module) => ({ default: module.OaRequestsPage })));
const Hub = lazy(() => import("./OaApprovalHubPage.tsx").then((module) => ({ default: module.OaApprovalHubPage })));

export function oaRoutes(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return [<Route key="oa-hub" path="/office/approvals" element={<Hub client={client} user={user} />} />,
    ...oaKinds.map((kind) => <Route key={kind} path={`/office/requests/${kind}`} element={<Requests key={kind} client={client} user={user} kind={kind} />} />)];
}
