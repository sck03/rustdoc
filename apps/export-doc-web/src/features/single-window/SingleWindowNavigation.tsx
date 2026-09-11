import { Link } from "react-router-dom";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";

export function getSingleWindowTitle(pathname: string) {
  if (/\/single-window\/acd\/\d+/.test(pathname)) {
    return "报关代理委托草稿";
  }

  if (/\/single-window\/coo\/\d+/.test(pathname)) {
    return "海关原产地证草稿";
  }

  if (/\/single-window\/operation-center\/\d+/.test(pathname)) {
    return "单一窗口批次详情";
  }

  if (pathname.startsWith("/single-window/reference-catalog")) {
    return "申报词典";
  }

  return "单一窗口操作中心";
}

export function SingleWindowTabs({
  activeKey,
}: {
  activeKey: "operation-center" | "reference-catalog" | "customs-coo" | "agent-consignment";
}) {
  const canViewOperations = useModulePermission("document.single-window").canView;
  const canViewDictionary = useModulePermission("document.declaration-dictionary").canView;
  return (
    <nav className="workspace-tabs" aria-label="单一窗口分类">
      {canViewOperations && <Link
        className={activeKey === "operation-center" ? "workspace-tab workspace-tab-active" : "workspace-tab"}
        to="/single-window/operation-center"
        aria-current={activeKey === "operation-center" ? "page" : undefined}
      >
        操作中心
      </Link>}
      {canViewDictionary && <Link
        className={activeKey === "reference-catalog" ? "workspace-tab workspace-tab-active" : "workspace-tab"}
        to="/single-window/reference-catalog"
        aria-current={activeKey === "reference-catalog" ? "page" : undefined}
      >
        申报词典
      </Link>}
      {activeKey === "customs-coo" ? <span className="workspace-tab workspace-tab-active">COO 草稿</span> : null}
      {activeKey === "agent-consignment" ? <span className="workspace-tab workspace-tab-active">ACD 草稿</span> : null}
    </nav>
  );
}
