import { type ComponentType, lazy, type LazyExoticComponent, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../api/index.ts";
import { hasModulePermission } from "./PermissionAccessContext.tsx";
import { getDefaultWorkspaceRoute, type ProductEditionPresentation } from "./productEdition.ts";
import { PageState } from "../ui/PageState.tsx";

type NamedComponent<TModule, TExport extends keyof TModule> =
  TModule[TExport] extends ComponentType<infer TProps> ? ComponentType<TProps> : never;

const DashboardPage = lazyNamed(() => import("../features/dashboard/DashboardPage.tsx"), "DashboardPage");
const CustomerFollowUpPage = lazyNamed(() => import("../features/crm/CustomerFollowUpPage.tsx"), "CustomerFollowUpPage");
const SalesDashboardPage = lazyNamed(() => import("../features/crm/SalesDashboardPage.tsx"), "SalesDashboardPage");
const SupplierDirectoryPage = lazyNamed(() => import("../features/suppliers/SupplierDirectoryPage.tsx"), "SupplierDirectoryPage");
const EmailTemplatePage = lazyNamed(() => import("../features/email-templates/EmailTemplatePage.tsx"), "EmailTemplatePage");
const SalesOpportunityPage = lazyNamed(() => import("../features/opportunities/SalesOpportunityPage.tsx"), "SalesOpportunityPage");
const InvoiceListPage = lazyNamed(() => import("../features/invoices/InvoiceListPage.tsx"), "InvoiceListPage");
const InvoiceEditorPage = lazyNamed(() => import("../features/invoices/InvoiceEditorPage.tsx"), "InvoiceEditorPage");
const QueryPage = lazyNamed(() => import("../features/query/QueryPage.tsx"), "QueryPage");
const PaymentListPage = lazyNamed(() => import("../features/payments/PaymentListPage.tsx"), "PaymentListPage");
const PaymentEditorPage = lazyNamed(() => import("../features/payments/PaymentEditorPage.tsx"), "PaymentEditorPage");
const MasterDataRoute = lazyNamed(() => import("../features/master-data/MasterDataPages.tsx"), "MasterDataRoute");
const MasterDataEditorRoute = lazyNamed(
  () => import("../features/master-data/MasterDataPages.tsx"),
  "MasterDataEditorRoute",
);
const HsCodeKnowledgePage = lazyNamed(() => import("../features/master-data/HsCodeKnowledgePage.tsx"), "HsCodeKnowledgePage");
const SingleWindowRoute = lazyNamed(() => import("../features/single-window/SingleWindowPages.tsx"), "SingleWindowRoute");
const SingleWindowOperationCenterPage = lazyNamed(
  () => import("../features/single-window/SingleWindowPages.tsx"),
  "SingleWindowOperationCenterPage",
);
const SingleWindowOperationCenterDetailPage = lazyNamed(
  () => import("../features/single-window/SingleWindowPages.tsx"),
  "SingleWindowOperationCenterDetailPage",
);
const SingleWindowReferenceCatalogPage = lazyNamed(
  () => import("../features/single-window/SingleWindowReferenceCatalogPage.tsx"),
  "SingleWindowReferenceCatalogPage",
);
const CustomsCooPage = lazyNamed(() => import("../features/single-window/CustomsCooPage.tsx"), "CustomsCooPage");
const AgentConsignmentPage = lazyNamed(
  () => import("../features/single-window/AgentConsignmentPage.tsx"),
  "AgentConsignmentPage",
);
const ReportTemplateDesignerPage = lazyNamed(
  () => import("../features/reports/ReportTemplateDesignerPage.tsx"),
  "ReportTemplateDesignerPage",
);
const JobCenterPage = lazyNamed(() => import("../features/jobs/JobCenterPage.tsx"), "JobCenterPage");
const ExcelToolsPage = lazyNamed(() => import("../features/tools/excel/ExcelToolsPage.tsx"), "ExcelToolsPage");
const SmartOcrPage = lazyNamed(() => import("../features/tools/SmartOcrPage.tsx"), "SmartOcrPage");
const ContainerPackingPage = lazyNamed(
  () => import("../features/tools/container-packing/ContainerPackingPage.tsx"),
  "ContainerPackingPage",
);
const ExchangeRatePage = lazyNamed(() => import("../features/tools/ExchangeRatePage.tsx"), "ExchangeRatePage");
const EmailPage = lazyNamed(() => import("../features/tools/EmailPage.tsx"), "EmailPage");
const UpdateCenterPage = lazyNamed(() => import("../features/system/UpdateCenterPage.tsx"), "UpdateCenterPage");
const LicensePage = lazyNamed(() => import("../features/system/LicensePage.tsx"), "LicensePage");
const AboutPage = lazyNamed(() => import("../features/system/AboutPage.tsx"), "AboutPage");
const AuditLogPage = lazyNamed(() => import("../features/audit-logs/AuditLogPage.tsx"), "AuditLogPage");
const AccessControlPage = lazyNamed(() => import("../features/access-control/AccessControlPage.tsx"), "AccessControlPage");
const SettingsPage = lazyNamed(() => import("../features/settings/SettingsPage.tsx"), "SettingsPage");

export function AppWorkspaceRoutes({
  activeProduct,
  apiBaseUrl,
  canManageAuditLogs,
  client,
  routeAccessAllowed,
  user,
}: {
  activeProduct: ProductEditionPresentation;
  apiBaseUrl: string;
  canManageAuditLogs: boolean;
  client: ExportDocManagerApiClient;
  routeAccessAllowed: boolean;
  user: ApiUserDto;
}) {
  if (!routeAccessAllowed) {
    return <NoModuleAccessPage />;
  }

  const defaultRoute = getDefaultWorkspaceRoute(user.capabilities);
  return (
    <Suspense fallback={<RouteLoadingPanel />}>
      <Routes>
        <Route path="/" element={<Navigate to={defaultRoute} replace />} />
        <Route path="/dashboard" element={user.capabilities.canUseDocumentWorkspace
          ? <DashboardPage client={client} />
          : <Navigate to={defaultRoute} replace />} />
        <Route path="/crm/dashboard" element={user.capabilities.canUseSalesWorkspace
          ? <SalesDashboardPage client={client} />
          : <Navigate to="/dashboard" replace />} />
        <Route path="/suppliers" element={user.capabilities.canUseSalesWorkspace
          ? <SupplierDirectoryPage client={client} />
          : <Navigate to="/dashboard" replace />} />
        <Route path="/crm/email-templates" element={user.capabilities.canUseSalesWorkspace
          ? <EmailTemplatePage client={client} />
          : <Navigate to="/dashboard" replace />} />
        <Route path="/crm/opportunities" element={user.capabilities.canUseSalesWorkspace
          ? <SalesOpportunityPage client={client} />
          : <Navigate to="/dashboard" replace />} />
        <Route path="/crm/follow-ups" element={user.capabilities.canUseSalesWorkspace
          ? <CustomerFollowUpPage client={client} />
          : <Navigate to="/dashboard" replace />} />
        <Route path="/invoices" element={<InvoiceListPage client={client} />} />
        <Route path="/invoices/new" element={<InvoiceEditorPage client={client} mode="new" />} />
        <Route path="/invoices/:invoiceId" element={<InvoiceEditorPage client={client} mode="edit" />} />
        <Route path="/query/invoices" element={<QueryPage client={client} />} />
        <Route path="/payments" element={<PaymentListPage client={client} />} />
        <Route path="/payments/new" element={<PaymentEditorPage client={client} mode="new" />} />
        <Route path="/payments/:paymentId" element={<PaymentEditorPage client={client} mode="edit" />} />
        <Route path="/master-data" element={<MasterDataRoute client={client} />} />
        <Route path="/master-data/hs-knowledge/:section" element={<HsCodeKnowledgePage client={client} />} />
        <Route path="/master-data/:entityKey" element={<MasterDataRoute client={client} />} />
        <Route path="/master-data/:entityKey/new" element={<MasterDataEditorRoute client={client} mode="new" />} />
        <Route path="/master-data/:entityKey/:recordKey" element={<MasterDataEditorRoute client={client} mode="edit" />} />
        <Route path="/single-window" element={<SingleWindowRoute />} />
        <Route path="/single-window/operation-center" element={<SingleWindowOperationCenterPage client={client} />} />
        <Route path="/single-window/operation-center/:batchId" element={<SingleWindowOperationCenterDetailPage client={client} />} />
        <Route path="/single-window/reference-catalog" element={<SingleWindowReferenceCatalogPage client={client} />} />
        <Route path="/single-window/coo/:invoiceId" element={<CustomsCooPage client={client} />} />
        <Route path="/single-window/acd/:invoiceId" element={<AgentConsignmentPage client={client} />} />
        <Route
          path="/reports/templates"
          element={
            <ReportTemplateDesignerPage
              apiBaseUrl={apiBaseUrl}
              client={client}
              canManageTemplates={hasModulePermission(user.capabilities.moduleAccess, "document.reports", "manage")}
              canDesignTemplates={hasModulePermission(user.capabilities.moduleAccess, "document.reports", "operate")}
            />
          }
        />
        <Route path="/jobs" element={<JobCenterPage client={client} />} />
        <Route path="/tools/excel" element={<ExcelToolsPage client={client} />} />
        <Route path="/tools/ocr" element={<SmartOcrPage client={client} />} />
        <Route path="/tools/container-packing" element={<ContainerPackingPage client={client} />} />
        <Route path="/tools/exchange-rates" element={<ExchangeRatePage client={client} />} />
        <Route path="/tools/email" element={<EmailPage client={client} />} />
        <Route path="/system/update" element={<UpdateCenterPage client={client} />} />
        <Route path="/system/license" element={<LicensePage client={client} />} />
        <Route path="/system/about" element={<AboutPage client={client} product={activeProduct} />} />
        <Route path="/access-denied" element={<NoModuleAccessPage />} />
        <Route path="/audit-logs" element={<AuditLogPage client={client} canManageAuditLogs={canManageAuditLogs} />} />
        <Route
          path="/system/access-control"
          element={<AccessControlPage client={client} canManageUsers={user.capabilities.canManageUsers === true} />}
        />
        <Route
          path="/settings"
          element={
            <SettingsPage
              client={client}
              canManageSettings={user.capabilities.canManageSettings === true}
              canManageUsers={user.capabilities.canManageUsers === true}
              canUseDocumentWorkspace={user.capabilities.canUseDocumentWorkspace === true}
              productName={activeProduct.productName}
            />
          }
        />
        <Route path="*" element={<Navigate to={defaultRoute} replace />} />
      </Routes>
    </Suspense>
  );
}

function RouteLoadingPanel() {
  return (
    <section className="work-surface">
      <PageState tone="loading" title="正在加载页面" description="正在准备当前业务模块，请稍候。" />
    </section>
  );
}

function NoModuleAccessPage() {
  return (
    <section className="work-surface">
      <PageState tone="permission" title="当前账号尚未分配可用模块" description="请联系系统管理员启用权限模板或重新分配岗位权限。账号本身仍可安全退出登录。" />
    </section>
  );
}

function lazyNamed<TModule extends Record<string, unknown>, TExport extends keyof TModule>(
  loader: () => Promise<TModule>,
  exportName: TExport,
): LazyExoticComponent<NamedComponent<TModule, TExport>> {
  return lazy(async () => ({
    default: (await loader())[exportName] as NamedComponent<TModule, TExport>,
  }));
}
