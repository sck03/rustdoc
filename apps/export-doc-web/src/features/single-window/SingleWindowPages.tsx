import { Navigate } from "react-router-dom";

export function SingleWindowRoute() {
  return <Navigate to="/single-window/operation-center" replace />;
}

export { getSingleWindowTitle,SingleWindowTabs } from "./SingleWindowNavigation.tsx";
export { SingleWindowOperationCenterDetailPage } from "./SingleWindowOperationCenterDetailPage.tsx";
export { SingleWindowOperationCenterPage } from "./SingleWindowOperationCenterPage.tsx";
