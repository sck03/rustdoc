import { type ComponentType, type FormEvent, lazy, Suspense, useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  ApiError,
  ApiUserDto,
  createExportDocManagerApiClient,
} from "./api/index.ts";
import { queryKeys } from "./api/queryKeys.ts";
import {
  getDesktopRuntimeContext,
  isDesktopBridgeAvailable,
} from "./desktop/desktopBridge.ts";
import { LoginPage } from "./features/auth/LoginPage.tsx";
import { readDesktopError } from "./ui/DesktopPathActions.tsx";
import { readStoredJson, removeStoredValue, writeStoredJson } from "./ui/browserStorage.ts";
import { readApiError } from "./ui/formUtils.ts";
import { useConfirmUnsavedChanges } from "./ui/unsavedChangesGuard.tsx";
import { WorkspaceShell } from "./app/WorkspaceShell.tsx";
import { hasModulePermission, PermissionAccessProvider } from "./app/PermissionAccessContext.tsx";
import { getRequiredModule, getRequiredRouteAccessLevel, getRequiredWorkspace, isAdminOnlyRoute, isDashboardRoute, isDesktopOnlyRoute, isFullEditionOnlyRoute, isLicenseRoute } from "./app/workspaceNavigation.ts";
import {
  getDefaultWorkspaceRoute,
  getProductEditionPresentation,
  type ProductEdition,
} from "./app/productEdition.ts";

const DashboardPage = lazyNamed(() => import("./features/dashboard/DashboardPage.tsx"), "DashboardPage");
const CustomerFollowUpPage = lazyNamed(() => import("./features/crm/CustomerFollowUpPage.tsx"), "CustomerFollowUpPage");
const SalesDashboardPage = lazyNamed(() => import("./features/crm/SalesDashboardPage.tsx"), "SalesDashboardPage");
const SupplierDirectoryPage = lazyNamed(() => import("./features/suppliers/SupplierDirectoryPage.tsx"), "SupplierDirectoryPage");
const EmailTemplatePage = lazyNamed(() => import("./features/email-templates/EmailTemplatePage.tsx"), "EmailTemplatePage");
const SalesOpportunityPage = lazyNamed(() => import("./features/opportunities/SalesOpportunityPage.tsx"), "SalesOpportunityPage");
const InvoiceListPage = lazyNamed(() => import("./features/invoices/InvoiceListPage.tsx"), "InvoiceListPage");
const InvoiceEditorPage = lazyNamed(() => import("./features/invoices/InvoiceEditorPage.tsx"), "InvoiceEditorPage");
const QueryPage = lazyNamed(() => import("./features/query/QueryPage.tsx"), "QueryPage");
const PaymentListPage = lazyNamed(() => import("./features/payments/PaymentListPage.tsx"), "PaymentListPage");
const PaymentEditorPage = lazyNamed(() => import("./features/payments/PaymentEditorPage.tsx"), "PaymentEditorPage");
const MasterDataRoute = lazyNamed(() => import("./features/master-data/MasterDataPages.tsx"), "MasterDataRoute");
const MasterDataEditorRoute = lazyNamed(
  () => import("./features/master-data/MasterDataPages.tsx"),
  "MasterDataEditorRoute",
);
const SingleWindowRoute = lazyNamed(() => import("./features/single-window/SingleWindowPages.tsx"), "SingleWindowRoute");
const SingleWindowOperationCenterPage = lazyNamed(
  () => import("./features/single-window/SingleWindowPages.tsx"),
  "SingleWindowOperationCenterPage",
);
const SingleWindowOperationCenterDetailPage = lazyNamed(
  () => import("./features/single-window/SingleWindowPages.tsx"),
  "SingleWindowOperationCenterDetailPage",
);
const SingleWindowCollaborationPage = lazyNamed(
  () => import("./features/single-window/SingleWindowPages.tsx"),
  "SingleWindowCollaborationPage",
);
const SingleWindowReferenceCatalogPage = lazyNamed(
  () => import("./features/single-window/SingleWindowReferenceCatalogPage.tsx"),
  "SingleWindowReferenceCatalogPage",
);
const CustomsCooPage = lazyNamed(() => import("./features/single-window/CustomsCooPage.tsx"), "CustomsCooPage");
const AgentConsignmentPage = lazyNamed(
  () => import("./features/single-window/AgentConsignmentPage.tsx"),
  "AgentConsignmentPage",
);
const ReportTemplateDesignerPage = lazyNamed(
  () => import("./features/reports/ReportTemplateDesignerPage.tsx"),
  "ReportTemplateDesignerPage",
);
const JobCenterPage = lazyNamed(() => import("./features/jobs/JobCenterPage.tsx"), "JobCenterPage");
const ExcelToolsPage = lazyNamed(() => import("./features/tools/excel/ExcelToolsPage.tsx"), "ExcelToolsPage");
const SmartOcrPage = lazyNamed(() => import("./features/tools/SmartOcrPage.tsx"), "SmartOcrPage");
const ContainerPackingPage = lazyNamed(
  () => import("./features/tools/container-packing/ContainerPackingPage.tsx"),
  "ContainerPackingPage",
);
const ExchangeRatePage = lazyNamed(() => import("./features/tools/ExchangeRatePage.tsx"), "ExchangeRatePage");
const EmailPage = lazyNamed(() => import("./features/tools/EmailPage.tsx"), "EmailPage");
const UpdateCenterPage = lazyNamed(() => import("./features/system/UpdateCenterPage.tsx"), "UpdateCenterPage");
const LicensePage = lazyNamed(() => import("./features/system/LicensePage.tsx"), "LicensePage");
const AboutPage = lazyNamed(() => import("./features/system/AboutPage.tsx"), "AboutPage");
const AuditLogPage = lazyNamed(() => import("./features/audit-logs/AuditLogPage.tsx"), "AuditLogPage");
const AccessControlPage = lazyNamed(() => import("./features/access-control/AccessControlPage.tsx"), "AccessControlPage");
const SettingsPage = lazyNamed(() => import("./features/settings/SettingsPage.tsx"), "SettingsPage");

const sessionStorageKey = "exportdocmanager.web.session";
const defaultApiBaseUrl = readDefaultApiBaseUrl();
const defaultDesktopAccessToken = readDefaultDesktopAccessToken();

type SessionState = {
  accessToken: string;
  expiresAt: string;
  apiBaseUrl: string;
  user: ApiUserDto;
};

type LoadState = "idle" | "loading" | "ready" | "error";

function App() {
  const [apiBaseUrl, setApiBaseUrl] = useState(defaultApiBaseUrl);
  const [desktopAccessToken, setDesktopAccessToken] = useState<string | undefined>(defaultDesktopAccessToken);
  const [desktopProductEdition, setDesktopProductEdition] = useState<ProductEdition>("Full");
  const [desktopContextLoading, setDesktopContextLoading] = useState(() => isDesktopBridgeAvailable() && !defaultDesktopAccessToken);
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [session, setSession] = useState<SessionState | null>(() => readStoredSession());
  const [loginState, setLoginState] = useState<LoadState>("idle");
  const [message, setMessage] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  const isDesktopRuntime = isDesktopBridgeAvailable();
  const sessionAccessToken = session?.accessToken;
  const sessionApiBaseUrl = session?.apiBaseUrl;
  const workspacePathname = session ? location.pathname : "/dashboard";
  const canManageSystem = session?.user.capabilities?.canManageSettings === true;
  const isFullEdition = session?.user.capabilities?.productEdition?.trim().toLowerCase() === "full";
  const canManageAuditLogs = canManageSystem && isFullEdition;

  const client = useMemo(
    () =>
      createExportDocManagerApiClient({
        baseUrl: sessionApiBaseUrl ?? apiBaseUrl,
        accessToken: () => sessionAccessToken,
        desktopAccessToken: () => desktopAccessToken,
      }),
    [apiBaseUrl, desktopAccessToken, sessionAccessToken, sessionApiBaseUrl],
  );

  useEffect(() => {
    if (!isDesktopBridgeAvailable()) {
      return undefined;
    }

    let isStale = false;
    setDesktopContextLoading(true);
    void getDesktopRuntimeContext()
      .then((context) => {
        if (isStale || !context) {
          return;
        }

        const nextApiBaseUrl = context.apiBaseUrl.trim();
        const nextDesktopAccessToken = context.desktopAccessToken.trim() || undefined;
        setDesktopProductEdition(context.productEdition);
        if (nextApiBaseUrl) {
          setApiBaseUrl(nextApiBaseUrl);
          setSession((current) => {
            if (!current || current.apiBaseUrl === nextApiBaseUrl) {
              return current;
            }

            clearStoredSession();
            queryClient.clear();
            return null;
          });
        }

        setDesktopAccessToken(nextDesktopAccessToken);
      })
      .catch((error) => {
        if (!isStale) {
          if (!isDesktopRuntimeContextUnavailable(error)) {
            setMessage(`无法读取桌面运行上下文：${readDesktopError(error)}`);
          } else {
            console.warn("Desktop runtime context is unavailable.", error);
          }
        }
      })
      .finally(() => {
        if (!isStale) {
          setDesktopContextLoading(false);
        }
      });

    return () => {
      isStale = true;
    };
  }, [queryClient]);

  useEffect(() => {
    if (session) {
      setApiBaseUrl(session.apiBaseUrl);
    }
  }, [session]);

  useEffect(() => {
    if (!session || desktopContextLoading) {
      return undefined;
    }

    let isStale = false;
    void client
      .getCurrentUser()
      .then((user) => {
        if (isStale) {
          return;
        }

        setSession((current) => {
          if (!current) {
            return current;
          }

          const nextSession = { ...current, user };
          writeStoredSession(nextSession);
          return nextSession;
        });
      })
      .catch((error) => {
        if (isStale) {
          return;
        }

        if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
          setSession(null);
          setMessage("登录状态已失效，请重新登录。");
          setLoginState("idle");
          clearStoredSession();
          queryClient.clear();
          navigate("/", { replace: true });
        }
      });

    return () => {
      isStale = true;
    };
  }, [client, desktopContextLoading, navigate, queryClient, sessionAccessToken, sessionApiBaseUrl]);

  useEffect(() => {
    if (!session) return;
    const requiredWorkspace = getRequiredWorkspace(location.pathname);
    const workspaceAllowed = requiredWorkspace === "sales"
      ? session.user.capabilities.canUseSalesWorkspace
      : requiredWorkspace === "document"
        ? session.user.capabilities.canUseDocumentWorkspace
        : true;
    const requiredModule = getRequiredModule(location.pathname);
    const enabledModules = session.user.capabilities.enabledModules ?? [];
    const requiredAccessLevel = getRequiredRouteAccessLevel(location.pathname);
    const moduleAllowed = !requiredModule ||
      (session.user.capabilities.moduleAccess?.length
        ? hasModulePermission(session.user.capabilities.moduleAccess, requiredModule, requiredAccessLevel)
        : enabledModules.length === 0 || enabledModules.some((moduleKey) => moduleKey.toLowerCase() === requiredModule.toLowerCase()));
    if (!workspaceAllowed || !moduleAllowed) {
      setMessage("当前产品版本或权限模板未启用该模块。");
      navigate(getDefaultWorkspaceRoute(session.user.capabilities), { replace: true });
    }
  }, [location.pathname, navigate, session]);

  useEffect(() => {
    if (!sessionAccessToken || desktopContextLoading || isLicenseRoute(location.pathname)) {
      return undefined;
    }

    let isStale = false;
    void client
      .getLicenseStatus()
      .then((status) => {
        if (isStale) {
          return;
        }

        queryClient.setQueryData(queryKeys.licenseStatus(), status);
        if (status.isTrialExpired) {
          setMessage(status.message || "试用期已过，请先注册授权。");
          navigate("/system/license", { replace: true });
        }
      })
      .catch((error) => {
        if (isStale) {
          return;
        }

        if (error instanceof ApiError && error.status === 402) {
          setMessage(readApiError(error));
          navigate("/system/license", { replace: true });
        }
      });

    return () => {
      isStale = true;
    };
  }, [client, desktopContextLoading, location.pathname, navigate, queryClient, sessionAccessToken]);

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoginState("loading");
    setMessage(null);

    const loginClient = createExportDocManagerApiClient({
      baseUrl: apiBaseUrl,
      desktopAccessToken: () => desktopAccessToken,
    });
    try {
      const response = await loginClient.login({
        body: {
          username,
          password,
        },
      });
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl,
        user: response.user,
      };
      setSession(nextSession);
      writeStoredSession(nextSession);
      queryClient.clear();
      setLoginState("ready");
      navigate(getDefaultWorkspaceRoute(response.user.capabilities), { replace: true });
    } catch (error) {
      setLoginState("error");
      setMessage(readApiError(error));
    }
  }

  function handleLogout() {
    if (!confirmDiscardChanges("退出登录")) {
      return;
    }

    if (session) {
      void client.logout().catch(() => undefined);
    }

    setSession(null);
    setMessage(null);
    setLoginState("idle");
    clearStoredSession();
    queryClient.clear();
    navigate("/", { replace: true });
  }

  useEffect(() => {
    if (session || isDashboardRoute(location.pathname)) {
      return;
    }

    navigate("/dashboard", { replace: true });
  }, [location.pathname, navigate, session]);

  useEffect(() => {
    if (!session) {
      return;
    }

    const hasAdminAccess = !isAdminOnlyRoute(location.pathname) || canManageSystem;
    const hasRuntimeAccess = !isDesktopOnlyRoute(location.pathname) || isDesktopRuntime;
    const hasEditionAccess = !isFullEditionOnlyRoute(location.pathname) || isFullEdition;
    if (hasAdminAccess && hasRuntimeAccess && hasEditionAccess) return;

    navigate(getDefaultWorkspaceRoute(session.user.capabilities), { replace: true });
  }, [canManageSystem, isDesktopRuntime, isFullEdition, location.pathname, navigate, session]);

  const isBusy = loginState === "loading" || desktopContextLoading;
  const loginProduct = getProductEditionPresentation(desktopProductEdition);

  if (!session) {
    return (
      <LoginPage
        apiBaseUrl={apiBaseUrl}
        username={username}
        password={password}
        isBusy={isBusy}
        message={message}
        product={loginProduct}
        onApiBaseUrlChange={setApiBaseUrl}
        onUsernameChange={setUsername}
        onPasswordChange={setPassword}
        onSubmit={handleLogin}
      />
    );
  }

  return (
    <PermissionAccessProvider
      grants={session.user.capabilities.moduleAccess}
      canManageSettings={session.user.capabilities.canManageSettings}
    >
      <WorkspaceShell
        pathname={workspacePathname}
        apiBaseUrl={session.apiBaseUrl ?? apiBaseUrl}
        isDesktopRuntime={isDesktopRuntime}
        user={session.user}
        onLogout={handleLogout}
      >
      <Suspense fallback={<RouteLoadingPanel />}>
          <Routes>
              <Route path="/" element={<Navigate to={getDefaultWorkspaceRoute(session.user.capabilities)} replace />} />
              <Route path="/dashboard" element={session.user.capabilities.canUseDocumentWorkspace
                ? <DashboardPage client={client} />
                : <Navigate to={getDefaultWorkspaceRoute(session.user.capabilities)} replace />} />
              <Route path="/crm/dashboard" element={session.user.capabilities.canUseSalesWorkspace
                ? <SalesDashboardPage client={client} />
                : <Navigate to="/dashboard" replace />} />
              <Route path="/suppliers" element={session.user.capabilities.canUseSalesWorkspace
                ? <SupplierDirectoryPage client={client} />
                : <Navigate to="/dashboard" replace />} />
              <Route path="/crm/email-templates" element={session.user.capabilities.canUseSalesWorkspace
                ? <EmailTemplatePage client={client} />
                : <Navigate to="/dashboard" replace />} />
              <Route path="/crm/opportunities" element={session.user.capabilities.canUseSalesWorkspace
                ? <SalesOpportunityPage client={client} />
                : <Navigate to="/dashboard" replace />} />
              <Route
                path="/crm/follow-ups"
                element={session.user.capabilities.canUseSalesWorkspace
                  ? <CustomerFollowUpPage client={client} />
                  : <Navigate to="/dashboard" replace />}
              />
              <Route path="/invoices" element={<InvoiceListPage client={client} />} />
              <Route path="/invoices/new" element={<InvoiceEditorPage client={client} mode="new" />} />
              <Route path="/invoices/:invoiceId" element={<InvoiceEditorPage client={client} mode="edit" />} />
              <Route path="/query/invoices" element={<QueryPage client={client} />} />
              <Route path="/payments" element={<PaymentListPage client={client} />} />
              <Route path="/payments/new" element={<PaymentEditorPage client={client} mode="new" />} />
              <Route path="/payments/:paymentId" element={<PaymentEditorPage client={client} mode="edit" />} />
              <Route path="/master-data" element={<MasterDataRoute client={client} />} />
              <Route path="/master-data/:entityKey" element={<MasterDataRoute client={client} />} />
              <Route path="/master-data/:entityKey/new" element={<MasterDataEditorRoute client={client} mode="new" />} />
              <Route path="/master-data/:entityKey/:recordKey" element={<MasterDataEditorRoute client={client} mode="edit" />} />
              <Route path="/single-window" element={<SingleWindowRoute />} />
              <Route path="/single-window/operation-center" element={<SingleWindowOperationCenterPage client={client} />} />
              <Route
                path="/single-window/operation-center/:batchId"
                element={<SingleWindowOperationCenterDetailPage client={client} />}
              />
              <Route path="/single-window/collaboration" element={<SingleWindowCollaborationPage client={client} />} />
              <Route
                path="/single-window/reference-catalog"
                element={
                  <SingleWindowReferenceCatalogPage
                    client={client}
                    canManageReferenceCatalog={session.user.capabilities?.canManageSettings === true}
                  />
                }
              />
              <Route path="/single-window/coo/:invoiceId" element={<CustomsCooPage client={client} />} />
              <Route path="/single-window/acd/:invoiceId" element={<AgentConsignmentPage client={client} />} />
              <Route
                path="/reports/templates"
                element={
                  <ReportTemplateDesignerPage
                    apiBaseUrl={session.apiBaseUrl}
                    client={client}
                    canManageTemplates={hasModulePermission(
                      session.user.capabilities.moduleAccess,
                      "document.reports",
                      "manage",
                    )}
                    canDesignTemplates={hasModulePermission(
                      session.user.capabilities.moduleAccess,
                      "document.reports",
                      "operate",
                    )}
                  />
                }
              />
              <Route path="/jobs" element={<JobCenterPage client={client} />} />
              <Route path="/tools/excel" element={<ExcelToolsPage client={client} />} />
              <Route path="/tools/ocr" element={<SmartOcrPage client={client} />} />
              <Route path="/tools/container-packing" element={<ContainerPackingPage client={client} />} />
              <Route path="/tools/exchange-rates" element={<ExchangeRatePage client={client} />} />
              <Route path="/tools/email" element={<EmailPage client={client} />} />
              <Route path="/system/update" element={<UpdateCenterPage />} />
              <Route path="/system/license" element={<LicensePage client={client} />} />
              <Route path="/system/about" element={<AboutPage client={client} />} />
              <Route
                path="/audit-logs"
                element={
                  <AuditLogPage
                    client={client}
                    canManageAuditLogs={canManageAuditLogs}
                  />
                }
              />
              <Route
                path="/system/access-control"
                element={
                  <AccessControlPage
                    client={client}
                    canManageUsers={session.user.capabilities?.canManageUsers === true}
                  />
                }
              />
              <Route
                path="/settings"
                element={
                  <SettingsPage
                    client={client}
                    canManageSettings={session.user.capabilities?.canManageSettings === true}
                    canManageUsers={session.user.capabilities?.canManageUsers === true}
                    canUseDocumentWorkspace={session.user.capabilities?.canUseDocumentWorkspace === true}
                  />
                }
              />
              <Route path="*" element={<Navigate to={getDefaultWorkspaceRoute(session.user.capabilities)} replace />} />
          </Routes>
      </Suspense>
      </WorkspaceShell>
    </PermissionAccessProvider>
  );
}

function RouteLoadingPanel() {
  return (
    <section className="work-surface">
      <div className="loading-panel">正在加载页面...</div>
    </section>
  );
}

function readStoredSession(): SessionState | null {
  try {
    const session = readStoredJson<SessionState>(sessionStorageKey, "session");
    removeStoredValue(sessionStorageKey, "local");
    if (!session) {
      return null;
    }
    if (session.expiresAt && new Date(session.expiresAt).getTime() <= Date.now()) {
      clearStoredSession();
      return null;
    }

    return session;
  } catch {
    return null;
  }
}

function writeStoredSession(session: SessionState) {
  removeStoredValue(sessionStorageKey, "local");
  writeStoredJson(sessionStorageKey, session, "session");
}

function clearStoredSession() {
  removeStoredValue(sessionStorageKey, "session");
  removeStoredValue(sessionStorageKey, "local");
}

function readDefaultApiBaseUrl() {
  const queryApiBaseUrl = new URLSearchParams(window.location.search).get("apiBaseUrl")?.trim();
  if (queryApiBaseUrl) {
    return queryApiBaseUrl;
  }

  return import.meta.env.VITE_EXPORTDOC_API_BASE_URL ?? window.location.origin;
}

function readDefaultDesktopAccessToken() {
  return new URLSearchParams(window.location.search).get("desktopAccessToken")?.trim() || undefined;
}

function isDesktopRuntimeContextUnavailable(error: unknown) {
  const message = readDesktopError(error).toLowerCase();
  return (
    message.includes("get_desktop_runtime_context") &&
    (message.includes("not allowed") || message.includes("plugin not found"))
  );
}

function lazyNamed<TModule extends Record<string, unknown>, TExport extends keyof TModule>(
  loader: () => Promise<TModule>,
  exportName: TExport,
) {
  return lazy(async () => ({
    default: (await loader())[exportName] as ComponentType<Record<string, unknown>>,
  }));
}

export default App;
