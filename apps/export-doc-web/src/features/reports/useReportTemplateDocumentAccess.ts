import { useMemo } from "react";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { resolveReportTypeOptions } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateDocumentAccess(canViewTemplates: boolean) {
  const invoiceAccess = usePermission("document.invoices", permissionActions.view);
  const paymentAccess = usePermission("document.payments", permissionActions.view);
  const invoicePreviewPermission = usePermission(permissionResources.invoiceOutput, permissionActions.preview);
  const paymentPreviewPermission = usePermission(permissionResources.paymentOutput, permissionActions.preview);
  const availableReportTypeOptions = useMemo(
    () => resolveReportTypeOptions(canViewTemplates, invoiceAccess.allowed, paymentAccess.allowed),
    [canViewTemplates, invoiceAccess.allowed, paymentAccess.allowed],
  );
  return { availableReportTypeOptions, invoicePreviewPermission, paymentPreviewPermission };
}
