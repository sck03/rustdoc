import { ExportDocManagerApiClient } from "../../api/index.ts";
import { ReportTemplateWorkspacePage } from "./ReportTemplateWorkspacePage.tsx";

export function ReportTemplateManagementPage({
  client,
  canManageTemplates,
  canDesignTemplates,
  canManageSettings,
}: {
  client: ExportDocManagerApiClient;
  canManageTemplates: boolean;
  canDesignTemplates: boolean;
  canManageSettings: boolean;
}) {
  return (
    <ReportTemplateWorkspacePage
      client={client}
      canManageTemplates={canManageTemplates}
      canDesignTemplates={canDesignTemplates}
      canManageSettings={canManageSettings}
      view="management"
    />
  );
}
