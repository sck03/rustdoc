import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import {
  ApiError,
  createExportDocManagerApiClient,
} from "./api/index.ts";
import { queryKeys } from "./api/queryKeys.ts";
import { subscribeToAuthenticationFailure } from "./api/authenticationFailureEvents.ts";
import { calculateSessionExpiryDelay, calculateSessionWarningDelay } from "./api/sessionExpiryModel.ts";
import {
  getDesktopRuntimeContext,
  isDesktopBridgeAvailable,
  requestAppExit,
  subscribeToAppExitRequests,
} from "./desktop/desktopBridge.ts";
import { LoginPage } from "./features/auth/LoginPage.tsx";
import { readDesktopError } from "./ui/DesktopPathActions.tsx";
import { readApiError } from "./ui/formUtils.ts";
import { useConfirmUnsavedChanges, useHasUnsavedChanges } from "./ui/unsavedChangesGuard.tsx";
import { WorkspaceShell, type WorkspaceNotice, type WorkspaceSessionAttention } from "./app/WorkspaceShell.tsx";
import { PermissionAccessProvider } from "./app/PermissionAccessContext.tsx";
import { AppWorkspaceRoutes } from "./app/AppWorkspaceRoutes.tsx";
import { isRouteAccessAllowed, isWorkspaceModuleAccessAllowed } from "./app/routeAccess.ts";
import {
  clearStoredSession,
  readStoredSession,
  type WebSessionState,
  writeStoredSession,
} from "./app/webSessionStorage.ts";
import { openSessionChannel, type SessionChannelMessage } from "./app/sessionChannel.ts";
import { useBusinessDateSessionRefresh } from "./app/useBusinessDateSessionRefresh.ts";
import { isDashboardRoute, isAdminOnlyRoute, isDesktopOnlyRoute, isFullEditionOnlyRoute, isLicenseRoute } from "./app/workspaceNavigation.ts";
import {
  getDefaultWorkspaceRoute,
  getProductEditionPresentation,
  type ProductEdition,
} from "./app/productEdition.ts";

const defaultApiBaseUrl = readDefaultApiBaseUrl();

type SessionState = WebSessionState;

type LoadState = "idle" | "loading" | "ready" | "error";

function App() {
  const [apiBaseUrl, setApiBaseUrl] = useState(defaultApiBaseUrl);
  const [desktopAccessToken, setDesktopAccessToken] = useState<string | undefined>(undefined);
  const [desktopProductEdition, setDesktopProductEdition] = useState<ProductEdition>("Full");
  const [desktopContextLoading, setDesktopContextLoading] = useState(() => isDesktopBridgeAvailable());
  const [username, setUsername] = useState(() => isDesktopBridgeAvailable() ? "admin" : "");
  const [password, setPassword] = useState("");
  const [bootstrapToken, setBootstrapToken] = useState("");
  const [session, setSession] = useState<SessionState | null>(() => readStoredSession());
  const [loginState, setLoginState] = useState<LoadState>("idle");
  const [message, setMessage] = useState<string | null>(null);
  const [workspaceNotice, setWorkspaceNotice] = useState<WorkspaceNotice | null>(null);
  const [sessionAttentionState, setSessionAttentionState] = useState<"warning" | "expired" | null>(null);
  const [sessionActionBusy, setSessionActionBusy] = useState(false);
  const [sessionActionError, setSessionActionError] = useState<string | null>(null);
  const [reauthPassword, setReauthPassword] = useState("");
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  const hasUnsavedChanges = useHasUnsavedChanges();
  const sessionChannelRef = useRef<ReturnType<typeof openSessionChannel>>(null);
  const sessionRef = useRef<SessionState | null>(session);
  const apiBaseUrlRef = useRef(apiBaseUrl);
  const hasUnsavedChangesRef = useRef(hasUnsavedChanges);
  sessionRef.current = session;
  apiBaseUrlRef.current = apiBaseUrl;
  hasUnsavedChangesRef.current = hasUnsavedChanges;
  const isDesktopRuntime = isDesktopBridgeAvailable();
  const sessionAccessToken = session?.accessToken;
  const sessionApiBaseUrl = session?.apiBaseUrl;
  const workspacePathname = session ? location.pathname : "/dashboard";
  const canManageSystem = session?.user.capabilities?.canManageSettings === true;
  const isFullEdition = session?.user.capabilities?.productEdition?.trim().toLowerCase() === "full";
  const canManageAuditLogs = canManageSystem && isFullEdition;
  const activeProduct = getProductEditionPresentation(
    session?.user.capabilities?.productEdition ?? desktopProductEdition,
  );

  useEffect(() => {
    if (!session) {
      document.title = activeProduct.displayName;
    }
  }, [activeProduct.displayName, session]);

  useEffect(() => {
    if (!isDesktopRuntime) {
      return undefined;
    }

    let disposed = false;
    let handlingExitRequest = false;
    let unsubscribe: (() => void) | undefined;
    void subscribeToAppExitRequests(async () => {
      if (disposed || handlingExitRequest) {
        return;
      }

      handlingExitRequest = true;
      try {
        if (await confirmDiscardChanges("退出程序")) {
          await requestAppExit();
        }
      } finally {
        handlingExitRequest = false;
      }
    }).then((nextUnsubscribe) => {
      if (disposed) {
        nextUnsubscribe();
      } else {
        unsubscribe = nextUnsubscribe;
      }
    }).catch((error) => {
      console.error("Failed to subscribe to native exit requests.", error);
    });

    return () => {
      disposed = true;
      unsubscribe?.();
    };
  }, [confirmDiscardChanges, isDesktopRuntime]);

  const endSession = useCallback((reason: string | null, broadcast = true) => {
    const currentApiBaseUrl = sessionRef.current?.apiBaseUrl ?? apiBaseUrlRef.current;
    setSession(null);
    setMessage(reason);
    setWorkspaceNotice(null);
    setSessionAttentionState(null);
    setSessionActionError(null);
    setSessionActionBusy(false);
    setReauthPassword("");
    setLoginState("idle");
    clearStoredSession();
    queryClient.clear();
    if (broadcast) {
      sessionChannelRef.current?.post({ type: "session-cleared", apiBaseUrl: currentApiBaseUrl });
    }
    navigate("/", { replace: true });
  }, [navigate, queryClient]);

  const expireSession = useCallback((reason: string) => {
    const currentSession = sessionRef.current;
    if (currentSession && hasUnsavedChangesRef.current) {
      clearStoredSession();
      setWorkspaceNotice(null);
      setSessionAttentionState("expired");
      setSessionActionError(null);
      setSessionActionBusy(false);
      setReauthPassword("");
      sessionChannelRef.current?.post({
        type: "session-expired",
        apiBaseUrl: currentSession.apiBaseUrl,
        reason,
      });
      return;
    }

    endSession(reason);
  }, [endSession]);

  useEffect(() => {
    const channel = openSessionChannel((event: SessionChannelMessage) => {
      const activeApiBaseUrl = sessionRef.current?.apiBaseUrl ?? apiBaseUrlRef.current;
      const eventApiBaseUrl = event.type === "session-updated"
        ? event.session.apiBaseUrl
        : event.apiBaseUrl;
      if (eventApiBaseUrl !== activeApiBaseUrl) {
        return;
      }

      if (event.type === "session-updated") {
        setSession(event.session);
        writeStoredSession(event.session);
        setSessionAttentionState(null);
        setSessionActionError(null);
        queryClient.clear();
        navigate(getDefaultWorkspaceRoute(event.session.user.capabilities), { replace: true });
        return;
      }

      if (event.type === "session-expired" && sessionRef.current && hasUnsavedChangesRef.current) {
        clearStoredSession();
        setSessionAttentionState("expired");
        setSessionActionError(null);
        setSessionActionBusy(false);
        setReauthPassword("");
        return;
      }

      endSession(
        event.type === "session-expired"
          ? event.reason
          : "已在其他标签页退出登录，请重新登录后继续。",
        false,
      );
    });
    sessionChannelRef.current = channel;
    return () => {
      channel?.close();
      if (sessionChannelRef.current === channel) {
        sessionChannelRef.current = null;
      }
    };
  }, [endSession, navigate, queryClient]);

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
          if (sessionRef.current && sessionRef.current.apiBaseUrl !== nextApiBaseUrl) {
            clearStoredSession();
            queryClient.clear();
            setSession(null);
          }
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
    if (!sessionAccessToken) {
      return undefined;
    }

    return subscribeToAuthenticationFailure(() => {
      expireSession("登录状态已失效，请重新登录后继续。为保护账号安全，系统没有重复提交刚才的操作。");
    });
  }, [expireSession, sessionAccessToken]);

  useEffect(() => {
    if (!session?.expiresAt) {
      return undefined;
    }

    let expiryTimerId: number | undefined;
    let warningTimerId: number | undefined;
    const scheduleExpiry = () => {
      const delay = calculateSessionExpiryDelay(session.expiresAt);
      if (delay === null) {
        return;
      }
      if (delay === 0) {
        expireSession("登录已到期，请重新登录后继续。为保护业务数据，系统已结束当前会话。");
        return;
      }
      expiryTimerId = window.setTimeout(scheduleExpiry, delay);
    };
    const scheduleWarning = () => {
      const delay = calculateSessionWarningDelay(session.expiresAt);
      if (delay === null) {
        return;
      }
      if (delay === 0) {
        setSessionAttentionState((current) => current === "expired" ? current : "warning");
        return;
      }
      warningTimerId = window.setTimeout(scheduleWarning, delay);
    };
    scheduleWarning();
    scheduleExpiry();
    return () => {
      if (expiryTimerId !== undefined) {
        window.clearTimeout(expiryTimerId);
      }
      if (warningTimerId !== undefined) {
        window.clearTimeout(warningTimerId);
      }
    };
  }, [expireSession, session?.expiresAt]);

  useBusinessDateSessionRefresh({
    client,
    desktopContextLoading,
    queryClient,
    session,
    sessionRef,
    setSession,
  });

  useEffect(() => {
    if (!session) return;
    if (!isWorkspaceModuleAccessAllowed(location.pathname, session.user)) {
      setWorkspaceNotice({
        id: "permission",
        tone: "warning",
        title: "当前页面不可用",
        message: "当前产品版本或权限模板未启用该模块，系统已返回当前账号可以使用的工作区。",
      });
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
          setWorkspaceNotice({
            id: "license",
            tone: "warning",
            title: "授权状态需要处理",
            message: status.message || "试用期已过，请先注册授权。",
          });
          navigate(canManageSystem ? "/system/license" : "/access-denied", { replace: true });
        } else {
          setWorkspaceNotice((current) => current?.id === "license" ? null : current);
        }
      })
      .catch((error) => {
        if (isStale) {
          return;
        }

        if (error instanceof ApiError && error.status === 402) {
          setWorkspaceNotice({
            id: "license",
            tone: "warning",
            title: "授权状态需要处理",
            message: readApiError(error),
          });
          navigate(canManageSystem ? "/system/license" : "/access-denied", { replace: true });
        }
      });

    return () => {
      isStale = true;
    };
  }, [canManageSystem, client, desktopContextLoading, location.pathname, navigate, queryClient, sessionAccessToken]);

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
      }, {
        headers: bootstrapToken.trim()
          ? { "X-ExportDocManager-Bootstrap-Token": bootstrapToken.trim() }
          : undefined,
      });
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl,
        user: response.user,
      };
      setSession(nextSession);
      setWorkspaceNotice(null);
      setSessionAttentionState(null);
      setSessionActionError(null);
      writeStoredSession(nextSession);
      sessionChannelRef.current?.post({ type: "session-updated", session: nextSession });
      setPassword("");
      setBootstrapToken("");
      queryClient.clear();
      setLoginState("ready");
      navigate(getDefaultWorkspaceRoute(response.user.capabilities), { replace: true });
    } catch (error) {
      setLoginState("error");
      setMessage(readApiError(error));
    }
  }

  async function handleLogout() {
    if (!await confirmDiscardChanges("退出登录")) {
      return;
    }

    if (session) {
      void client.logout().catch(() => undefined);
    }

    endSession(null);
  }

  async function handleRenewSession() {
    if (!session || sessionActionBusy) return;
    setSessionActionBusy(true);
    setSessionActionError(null);
    try {
      const response = await client.renewSession();
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl: session.apiBaseUrl,
        user: response.user,
      };
      setSession(nextSession);
      writeStoredSession(nextSession);
      sessionChannelRef.current?.post({ type: "session-updated", session: nextSession });
      setSessionAttentionState(null);
      setReauthPassword("");
    } catch (error) {
      setSessionActionError(readApiError(error));
    } finally {
      setSessionActionBusy(false);
    }
  }

  async function handleReauthenticate() {
    if (!session || sessionActionBusy || !reauthPassword) return;
    setSessionActionBusy(true);
    setSessionActionError(null);
    const loginClient = createExportDocManagerApiClient({
      baseUrl: session.apiBaseUrl,
      desktopAccessToken: () => desktopAccessToken,
    });
    try {
      const response = await loginClient.login({
        body: { username: session.user.username, password: reauthPassword },
      });
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl: session.apiBaseUrl,
        user: response.user,
      };
      setSession(nextSession);
      writeStoredSession(nextSession);
      sessionChannelRef.current?.post({ type: "session-updated", session: nextSession });
      setSessionAttentionState(null);
      setReauthPassword("");
    } catch (error) {
      setSessionActionError(readApiError(error));
    } finally {
      setSessionActionBusy(false);
    }
  }

  async function handleDiscardExpiredDraftAndLogout() {
    if (await confirmDiscardChanges("放弃草稿并重新登录")) {
      endSession("登录已到期，请重新登录后继续。");
    }
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

    const restriction = !hasAdminAccess
      ? "当前账号没有系统管理权限。"
      : !hasRuntimeAccess
        ? "该功能仅在桌面运行模式中提供。"
        : "当前产品版本未包含该功能。";
    setWorkspaceNotice({
      id: "permission",
      tone: "warning",
      title: "当前页面不可用",
      message: `${restriction}系统已返回当前账号可以使用的工作区。`,
    });
    navigate(getDefaultWorkspaceRoute(session.user.capabilities), { replace: true });
  }, [canManageSystem, isDesktopRuntime, isFullEdition, location.pathname, navigate, session]);

  const isBusy = loginState === "loading" || desktopContextLoading;
  const loginProduct = getProductEditionPresentation(desktopProductEdition);
  const routeAccessAllowed = !session || isRouteAccessAllowed({
    pathname: location.pathname,
    user: session.user,
    canManageSystem,
    isDesktopRuntime,
    isFullEdition,
  });
  const sessionAttention: WorkspaceSessionAttention | null = sessionAttentionState
    ? {
        state: sessionAttentionState,
        message: sessionAttentionState === "expired"
          ? "当前编辑内容仍保留在本页，但保存和导航已锁定。请用当前账号重新验证后继续。"
          : "为避免保存过程中会话中断，请现在续期；无需重新输入密码。",
        isBusy: sessionActionBusy,
        password: reauthPassword,
        errorMessage: sessionActionError,
        onPasswordChange: setReauthPassword,
        onContinue: () => { void handleRenewSession(); },
        onReauthenticate: () => { void handleReauthenticate(); },
        onDiscardAndLogout: () => { void handleDiscardExpiredDraftAndLogout(); },
      }
    : null;

  if (!session) {
    return (
      <LoginPage
        apiBaseUrl={apiBaseUrl}
        username={username}
        password={password}
        bootstrapToken={bootstrapToken}
        isDesktopRuntime={isDesktopRuntime}
        isBusy={isBusy}
        message={message}
        product={loginProduct}
        onApiBaseUrlChange={setApiBaseUrl}
        onUsernameChange={setUsername}
        onPasswordChange={setPassword}
        onBootstrapTokenChange={setBootstrapToken}
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
        notice={workspaceNotice}
        onDismissNotice={() => setWorkspaceNotice(null)}
        sessionAttention={sessionAttention}
      >
        <AppWorkspaceRoutes
          activeProduct={activeProduct}
          apiBaseUrl={session.apiBaseUrl}
          canManageAuditLogs={canManageAuditLogs}
          client={client}
          routeAccessAllowed={routeAccessAllowed}
          user={session.user}
        />
      </WorkspaceShell>
    </PermissionAccessProvider>
  );
}

function readDefaultApiBaseUrl() {
  return import.meta.env.VITE_EXPORTDOC_API_BASE_URL ?? window.location.origin;
}

function isDesktopRuntimeContextUnavailable(error: unknown) {
  const message = readDesktopError(error).toLowerCase();
  return (
    message.includes("get_desktop_runtime_context") &&
    (message.includes("not allowed") || message.includes("plugin not found"))
  );
}

export default App;
