import { Navigate } from "react-router-dom";

export function SingleWindowRoute() {
  return <Navigate to="/single-window/operation-center" replace />;
}

export { SingleWindowCollaborationPage } from "./SingleWindowCollaborationPage.tsx";
export { getSingleWindowTitle,SingleWindowTabs } from "./SingleWindowNavigation.tsx";
export { SingleWindowOperationCenterDetailPage } from "./SingleWindowOperationCenterDetailPage.tsx";
export { SingleWindowOperationCenterPage } from "./SingleWindowOperationCenterPage.tsx";
