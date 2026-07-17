import { Navigate,useParams } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getMasterDataConfig,getMasterDataConfigFromPath } from "./masterDataConfigs.ts";
import { MasterDataEditorPage } from "./MasterDataEditorPage.tsx";
import { MasterDataListPage } from "./MasterDataListPage.tsx";

export function MasterDataRoute({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.master-data");
  const { entityKey } = useParams();
  const config = getMasterDataConfig(entityKey);

  if (!entityKey) {
    return <Navigate to="/master-data/customers" replace />;
  }

  if (!config) {
    return <Navigate to="/master-data/customers" replace />;
  }

  return (
    <MasterDataListPage
      client={client}
      config={config}
      canOperate={permission.canOperate}
      canManage={permission.canManage}
    />
  );
}

export function MasterDataEditorRoute({
  client,
  mode,
}: {
  client: ExportDocManagerApiClient;
  mode: "new" | "edit";
}) {
  const permission = useModulePermission("document.master-data");
  const { entityKey } = useParams();
  const config = getMasterDataConfig(entityKey);

  if (!config) {
    return <Navigate to="/master-data/customers" replace />;
  }

  if (mode === "new" && !permission.canOperate) {
    return <Navigate to={`/master-data/${config.key}`} replace />;
  }

  return (
    <MasterDataEditorPage
      client={client}
      config={config}
      mode={mode}
      canOperate={permission.canOperate}
      canManage={permission.canManage}
    />
  );
}

export function getMasterDataTitle(pathname: string) {
  const config = getMasterDataConfigFromPath(pathname);
  if (!config) {
    return "主数据";
  }

  if (pathname.endsWith("/new")) {
    return config.newLabel;
  }

  if (/\/master-data\/[^/]+\/[^/]+/.test(pathname)) {
    return config.editLabel;
  }

  return config.label;
}

