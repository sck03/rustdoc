import { Link } from "react-router-dom";

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

  if (pathname.startsWith("/single-window/collaboration")) {
    return "单一窗口协同";
  }

  if (pathname.startsWith("/single-window/reference-catalog")) {
    return "参考词典";
  }

  return "单一窗口操作中心";
}

export function SingleWindowTabs({
  activeKey,
}: {
  activeKey: "operation-center" | "collaboration" | "reference-catalog" | "customs-coo" | "agent-consignment";
}) {
  return (
    <nav className="workspace-tabs" aria-label="单一窗口分类">
      <Link
        className={activeKey === "operation-center" ? "workspace-tab workspace-tab-active" : "workspace-tab"}
        to="/single-window/operation-center"
      >
        操作中心
      </Link>
      <Link
        className={activeKey === "collaboration" ? "workspace-tab workspace-tab-active" : "workspace-tab"}
        to="/single-window/collaboration"
      >
        协同看板
      </Link>
      <Link
        className={activeKey === "reference-catalog" ? "workspace-tab workspace-tab-active" : "workspace-tab"}
        to="/single-window/reference-catalog"
      >
        参考词典
      </Link>
      {activeKey === "customs-coo" ? <span className="workspace-tab workspace-tab-active">COO 草稿</span> : null}
      {activeKey === "agent-consignment" ? <span className="workspace-tab workspace-tab-active">ACD 草稿</span> : null}
    </nav>
  );
}
