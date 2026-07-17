import { ExportDocManagerApiClient } from "../../../api/index.ts";
import { useModulePermission } from "../../../app/PermissionAccessContext.tsx";
import { ContainerPackingPanel } from "./ContainerPackingPanel.tsx";

export function ContainerPackingPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.container-packing");

  return (
    <section className="work-surface container-packing-surface" aria-label="装柜模拟器">
      <ContainerPackingPanel
        client={client}
        canOperate={permission.canOperate}
        canManage={permission.canManage}
      />
    </section>
  );
}
