import type { BackgroundJobSnapshot } from "../../api/index.ts";
import { useModulePermission, usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { hasJobRetryPermission, type JobRetryPermissionSet } from "./jobPresentation.ts";

export function useJobPermissions() {
  const jobPermission = useModulePermission("document.jobs");
  const reportPermission = useModulePermission("document.reports");
  const retryPermissions: JobRetryPermissionSet = {
    canOperateJobs: jobPermission.canOperate,
    canOperateReports: reportPermission.canOperate,
    canOperateExcel: useModulePermission("document.excel").canOperate,
    canOperateQuery: useModulePermission("document.query").canOperate,
    canExportInvoicePdf: usePermission(permissionResources.invoiceOutput, permissionActions.exportPdf).allowed,
    canExportPaymentPdf: usePermission(permissionResources.paymentOutput, permissionActions.exportPdf).allowed,
    canExportInvoiceZip: usePermission(permissionResources.invoiceOutput, permissionActions.exportZip).allowed,
    canSendInvoiceEmail: usePermission(permissionResources.invoiceOutput, permissionActions.sendEmail).allowed,
    canSendEmail: usePermission(permissionResources.emailDelivery, permissionActions.send).allowed,
  };
  return {
    jobPermission,
    reportPermission,
    canExportInvoiceZip: retryPermissions.canExportInvoiceZip,
    canRetryJob: (job: BackgroundJobSnapshot) => hasJobRetryPermission(job.retryOperation, retryPermissions),
  };
}
