import { lazy } from "react";
import { Route } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaKinds } from "../oa/oaModel.ts";

const Requests = lazy(() => import("../oa/OaRequestsPage.tsx").then((module) => ({ default: module.OaRequestsPage })));
const Hub = lazy(() => import("../oa/OaApprovalHubPage.tsx").then((module) => ({ default: module.OaApprovalHubPage })));
const ApprovalSettings = lazy(() => import("../oa/ApprovalSettingsPage.tsx").then(module => ({ default: module.ApprovalSettingsPage })));
const Announcements = lazy(() => import("../communication/AnnouncementsPage.tsx").then(module => ({ default: module.AnnouncementsPage })));
const Notifications = lazy(() => import("../communication/NotificationsPage.tsx").then(module => ({ default: module.NotificationsPage })));

export function officeRoutes(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return [<Route key="oa-hub" path="/office/approvals" element={<Hub client={client} user={user} />} />,
    <Route key="oa-settings" path="/office/approval-settings" element={<ApprovalSettings client={client} user={user} />} />,
    <Route key="announcements" path="/office/announcements" element={<Announcements client={client} user={user} />} />,
    <Route key="notifications" path="/office/notifications" element={<Notifications client={client} user={user} />} />,
    ...oaKinds.map((kind) => <Route key={kind} path={`/office/requests/${kind}`} element={<Requests key={kind} client={client} user={user} kind={kind} />} />)];
}
