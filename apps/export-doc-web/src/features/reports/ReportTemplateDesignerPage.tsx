import { ExportDocManagerApiClient } from "../../api/index.ts";
import type { ReportTemplatePermissionAccess } from "./reportTemplateDesignerModel.ts";
import { ReportTemplateWorkspacePage } from "./ReportTemplateWorkspacePage.tsx";

export function ReportTemplateDesignerPage({
  client,
  templateAccess,
  canManageSettings,
}: {
  client: ExportDocManagerApiClient;
  templateAccess: ReportTemplatePermissionAccess;
  canManageSettings: boolean;
}) {
  return (
    <ReportTemplateWorkspacePage
      client={client}
      templateAccess={templateAccess}
      canManageSettings={canManageSettings}
    />
  );
}
