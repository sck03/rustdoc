import { type FormEvent, lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
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
  showReadyDesktopWindow,
  restartFailedDesktopStartup,
} from "./desktop/desktopBridge.ts";
import { LoginPage } from "./features/auth/LoginPage.tsx";
import { PageState } from "./ui/PageState.tsx";
import { StartupScreen } from "./app/StartupScreen.tsx";
import { readDesktopError } from "./ui/DesktopPathActions.tsx";
import { readApiError } from "./ui/formUtils.ts";
import { useConfirmUnsavedChanges, useHasUnsavedChanges } from "./ui/unsavedChangesGuard.tsx";
import type { WorkspaceNotice, WorkspaceSessionAttention } from "./app/WorkspaceShell.tsx";
import { isRouteAccessAllowed } from "./app/routeAccess.ts";
import {
  clearStoredSession,
  readStoredSession,
  type WebSessionState,
  writeStoredSession,
} from "./app/webSessionStorage.ts";
import { isCurrentSession, openSessionChannel, shouldAcceptSessionUpdate, type SessionChannelMessage } from "./app/sessionChannel.ts";
import { useBusinessDateSessionRefresh } from "./app/useBusinessDateSessionRefresh.ts";
import { useUnitContextSwitch } from "./app/useUnitContextSwitch.ts";
import { prefetchLandingDashboard } from "./app/loginPrefetch.ts";
import { createSessionApiClient } from "./app/sessionApiClient.ts";
import { isDashboardRoute, isLicenseRoute } from "./app/workspaceNavigation.ts";
import {
  getDefaultWorkspaceRoute,
  getProductEditionPresentation,
  type ProductEdition,
} from "./app/productEdition.ts";

const defaultApiBaseUrl = readDefaultApiBaseUrl();
const AuthenticatedWorkspace = lazy(() => import("./app/AuthenticatedWorkspace.tsx").then(module => ({ default: module.AuthenticatedWorkspace })));

type SessionState = WebSessionState;

type LoadState = "idle" | "loading" | "ready" | "error";

function App() {
  const [apiBaseUrl, setApiBaseUrl] = useState(defaultApiBaseUrl);
  const [desktopAccessToken, setDesktopAccessToken] = useState<string | undefined>(undefined);
  const [desktopProductEdition, setDesktopProductEdition] = useState<ProductEdition>("Full");
  const [desktopContextLoading, setDesktopContextLoading] = useState(() => isDesktopBridgeAvailable());
  const [startupError,setStartupError] = useState<string | null>(null);
  const [startupAttempt,setStartupAttempt] = useState(0);
  const [startupLog,setStartupLog] = useState<string | undefined>();
  const [restartStartup,setRestartStartup] = useState(false);
  const [username, setUsername] = useState(() => isDesktopBridgeAvailable() ? "admin" : "");
  const [password, setPassword] = useState("");
  const [bootstrapToken, setBootstrapToken] = useState("");
  const [session, setSessionState] = useState<SessionState | null>(() => readStoredSession());
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
  const validatedUserTokenRef = useRef<string | undefined>(undefined);
  const sessionActionRef = useRef<symbol | null>(null);
  const unitTransitionRef = useRef(false);
  const allowAuthenticationFailure = useCallback(() => !sessionActionRef.current && !unitTransitionRef.current, []);
  const apiBaseUrlRef = useRef(apiBaseUrl);
  const hasUnsavedChangesRef = useRef(hasUnsavedChanges);
  const setSession = useCallback((nextSession: SessionState | null) => {
    validatedUserTokenRef.current = nextSession?.accessToken;
    sessionRef.current = nextSession;
    setSessionState(nextSession);
    if (nextSession) writeStoredSession(nextSession);
    else clearStoredSession();
  }, []);
  const resetSessionAttention = useCallback((state: "expired" | null = null) => {
    setSessionAttentionState(state);
    setSessionActionError(null);
    setSessionActionBusy(false);
    setReauthPassword("");
  }, []);
  apiBaseUrlRef.current = apiBaseUrl;
  hasUnsavedChangesRef.current = hasUnsavedChanges;
  const isDesktopRuntime = isDesktopBridgeAvailable();
  useEffect(() => { void showReadyDesktopWindow().catch(error => console.error("Unable to show the prepared window",error)); }, []);
  const sessionAccessToken = session?.accessToken;
  const sessionApiBaseUrl = session?.apiBaseUrl;
  const workspacePathname = session ? location.pathname : "/dashboard";
  const canManageSystem = session?.user.capabilities?.canManageSettings === true;
  const canManageAuditLogs = canManageSystem && session?.user.capabilities.canManageUsers === true;
  const activeProduct = getProductEditionPresentation(
    session?.user.capabilities?.productEdition ?? desktopProductEdition,
    isDesktopRuntime,
  );

  useEffect(() => {
    if (!session) {
      document.title = desktopContextLoading ? "正在启动…" : activeProduct.displayName;
    }
  }, [activeProduct.displayName, desktopContextLoading, session]);

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
    const previousSession = sessionRef.current;
    sessionActionRef.current = null;
    setSession(null);
    setMessage(reason);
    setWorkspaceNotice(null);
    resetSessionAttention();
    setLoginState("idle");
    queryClient.clear();
    if (broadcast && previousSession) {
      sessionChannelRef.current?.post({ type: "session-cleared", apiBaseUrl: previousSession.apiBaseUrl, accessToken: previousSession.accessToken });
    }
    navigate("/", { replace: true });
  }, [navigate, queryClient, resetSessionAttention, setSession]);

  const expireSession = useCallback((reason: string) => {
    const currentSession = sessionRef.current;
    if (currentSession && hasUnsavedChangesRef.current) {
      clearStoredSession();
      setWorkspaceNotice(null);
      resetSessionAttention("expired");
      sessionChannelRef.current?.post({
        type: "session-expired",
        apiBaseUrl: currentSession.apiBaseUrl,
        accessToken: currentSession.accessToken,
        reason,
      });
      return;
    }

    endSession(reason);
  }, [endSession, resetSessionAttention]);

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
        const previousSession = sessionRef.current;
        if (!shouldAcceptSessionUpdate(previousSession, event)) return;
        if (previousSession && (previousSession.user.contextId ?? "") !== (event.session.user.contextId ?? "") && hasUnsavedChangesRef.current) {
          clearStoredSession();
          resetSessionAttention("expired");
          return;
        }
        sessionActionRef.current = null;
        setSession(event.session);
        resetSessionAttention();
        if (!previousSession || previousSession.user.contextId !== event.session.user.contextId || JSON.stringify(previousSession.user.capabilities) !== JSON.stringify(event.session.user.capabilities)) {
          void queryClient.cancelQueries();
          queryClient.clear();
        }
        if (!previousSession) navigate(getDefaultWorkspaceRoute(event.session.user.capabilities), { replace: true });
        return;
      }

      if (event.accessToken !== sessionRef.current?.accessToken) return;

      if (event.type === "session-expired" && sessionRef.current && hasUnsavedChangesRef.current) {
        clearStoredSession();
        resetSessionAttention("expired");
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
  }, [endSession, navigate, queryClient, resetSessionAttention, setSession]);

  const client = useMemo(
    () =>
      createSessionApiClient({
        baseUrl: sessionApiBaseUrl ?? apiBaseUrl,
        accessToken: () => sessionAccessToken,
        desktopAccessToken: () => desktopAccessToken,
      }, () => !sessionActionRef.current && !unitTransitionRef.current && sessionRef.current?.accessToken === sessionAccessToken && sessionRef.current?.apiBaseUrl === (sessionApiBaseUrl ?? apiBaseUrl)),
    [apiBaseUrl, desktopAccessToken, sessionAccessToken, sessionApiBaseUrl],
  );
  const unitContext = useUnitContextSwitch(client, session, (next, previous) => {
    setSession(next);
    resetSessionAttention();
    setWorkspaceNotice(null);
    sessionChannelRef.current?.post({ type: "session-expired", apiBaseUrl: previous.apiBaseUrl, accessToken: previous.accessToken, reason: "其他标签页已切换单位身份，请重新验证后继续当前单位的工作。" });
  }, sessionActionBusy, unitTransitionRef);

  useEffect(() => {
    if (!isDesktopBridgeAvailable()) {
      return undefined;
    }

    let isStale = false;
    setDesktopContextLoading(true);
    setStartupError(null);
    void getDesktopRuntimeContext()
      .then((context) => {
        if (isStale || !context) {
          return;
        }

        const nextApiBaseUrl = context.apiBaseUrl.trim();
        const nextDesktopAccessToken = context.desktopAccessToken.trim() || undefined;
        setDesktopProductEdition(context.productEdition);
        setStartupLog(context.logPath);
        setRestartStartup(Boolean(context.startupError));
        if(context.startupError) { setStartupError(context.startupError);return; }
        if (nextApiBaseUrl) {
          setApiBaseUrl(nextApiBaseUrl);
          if (sessionRef.current && sessionRef.current.apiBaseUrl !== nextApiBaseUrl) {
            queryClient.clear();
            setSession(null);
          }
        }

        setDesktopAccessToken(nextDesktopAccessToken);
      })
      .catch((error) => {
        if (!isStale) {
          setStartupError(`无法连接桌面服务：${readDesktopError(error)}`);
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
  }, [queryClient, setSession, startupAttempt]);

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
    validatedUserTokenRef,
    allowAuthenticationFailure,
  });

  const routeAccessAllowed = !session || isRouteAccessAllowed({
    pathname: location.pathname, user: session.user, canManageSystem, isDesktopRuntime,
  });
  useEffect(() => {
    if (!session || desktopContextLoading || unitContext.busy) return;
    if (!routeAccessAllowed) {
      setWorkspaceNotice({
        id: "permission",
        tone: "warning",
        title: "当前页面不可用",
        message: "当前账号、产品版本或运行模式不允许访问此页面，系统已返回可用的工作区。",
      });
      navigate(getDefaultWorkspaceRoute(session.user.capabilities), { replace: true });
    }
  }, [desktopContextLoading, routeAccessAllowed, location.pathname, navigate, session, unitContext.busy]);

  useEffect(() => {
    if (!sessionAccessToken || desktopContextLoading || isLicenseRoute(location.pathname)) {
      return undefined;
    }

    let isStale = false;
    void queryClient.fetchQuery({
      queryKey: queryKeys.licenseStatus(),
      queryFn: ({ signal }) => client.getLicenseStatus({ signal }),
      staleTime: 60_000,
    })
      .then((status) => {
        if (isStale) {
          return;
        }

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
    if (sessionActionRef.current) return;
    const action = Symbol();
    sessionActionRef.current = action;
    const previousSession = sessionRef.current;
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
      if (sessionActionRef.current !== action || !isCurrentSession(sessionRef.current, previousSession)) return;
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl,
        user: response.user,
      };
      const defaultRoute = getDefaultWorkspaceRoute(response.user.capabilities);
      setSession(nextSession);
      setWorkspaceNotice(null);
      resetSessionAttention();
      sessionChannelRef.current?.post({ type: "session-updated", session: nextSession, previousAccessToken: null });
      setPassword("");
      setBootstrapToken("");
      queryClient.clear();
      setLoginState("ready");
      // Warm the landing page query while its route chunk loads.
      if (defaultRoute === "/dashboard" || defaultRoute === "/crm/dashboard") {
        prefetchLandingDashboard({
          queryClient,
          client: createSessionApiClient({
            baseUrl: apiBaseUrl,
            accessToken: () => nextSession.accessToken,
            desktopAccessToken: () => desktopAccessToken,
          }, () => sessionRef.current?.accessToken === nextSession.accessToken && sessionRef.current?.apiBaseUrl === apiBaseUrl),
          route: defaultRoute,
        });
      }
      navigate(defaultRoute, { replace: true });
    } catch (error) {
      if (sessionActionRef.current !== action || !isCurrentSession(sessionRef.current, previousSession)) return;
      setLoginState("error");
      setMessage(readApiError(error));
    } finally {
      if (sessionActionRef.current === action) sessionActionRef.current = null;
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

  async function handleRefreshSession(reauthenticate = false) {
    const previousSession = sessionRef.current;
    if (!previousSession || sessionActionRef.current || unitContext.busy || (reauthenticate && !reauthPassword)) return;
    const action = Symbol();
    sessionActionRef.current = action;
    setSessionActionBusy(true);
    setSessionActionError(null);
    try {
      let response = reauthenticate
        ? await createExportDocManagerApiClient({
          baseUrl: previousSession.apiBaseUrl,
          desktopAccessToken: () => desktopAccessToken,
        }).login({ body: { username: previousSession.user.username, password: reauthPassword } })
        : await client.renewSession();
      if (sessionActionRef.current !== action || !isCurrentSession(sessionRef.current, previousSession)) return;
      if (response.user.id !== previousSession.user.id) {
        throw new Error("账号身份已变化，请放弃当前草稿后重新登录。");
      }
      if (reauthenticate && previousSession.user.contextId) {
        const renewedClient = createExportDocManagerApiClient({ baseUrl: previousSession.apiBaseUrl, accessToken: () => response.accessToken, desktopAccessToken: () => desktopAccessToken });
        try {
          response = await renewedClient.switchUnitContext({ body: { contextId: previousSession.user.contextId } });
        } catch (error) {
          void renewedClient.logout().catch(() => undefined);
          throw error;
        }
        if (sessionActionRef.current !== action || !isCurrentSession(sessionRef.current, previousSession)) return;
      }
      const nextSession: SessionState = {
        accessToken: response.accessToken,
        expiresAt: response.expiresAt,
        apiBaseUrl: previousSession.apiBaseUrl,
        user: response.user,
      };
      if (JSON.stringify(previousSession.user.capabilities) !== JSON.stringify(nextSession.user.capabilities)) {
        queryClient.clear();
      }
      setSession(nextSession);
      sessionChannelRef.current?.post({
        type: "session-updated", session: nextSession, previousAccessToken: previousSession.accessToken,
      });
      resetSessionAttention();
    } catch (error) {
      if (sessionActionRef.current === action && isCurrentSession(sessionRef.current, previousSession)) {
        setSessionActionError(readApiError(error));
      }
    } finally {
      if (sessionActionRef.current === action) {
        sessionActionRef.current = null;
        setSessionActionBusy(false);
      }
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

    navigate("/", { replace: true });
  }, [location.pathname, navigate, session]);

  const isBusy = loginState === "loading" || desktopContextLoading;
  const loginProduct = getProductEditionPresentation(desktopProductEdition, isDesktopRuntime);
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
        onContinue: () => { void handleRefreshSession(); },
        onReauthenticate: () => { void handleRefreshSession(true); },
        onDiscardAndLogout: () => { void handleDiscardExpiredDraftAndLogout(); },
      }
    : null;

  if (desktopContextLoading) {
    return <StartupScreen />;
  }
  if (startupError) return <StartupScreen name={loginProduct.displayName} error={startupError} logPath={startupLog}
    onRetry={() => { if(restartStartup) void restartFailedDesktopStartup().catch(error => setStartupError(readDesktopError(error)));else setStartupAttempt(value => value+1); }} />;

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
    <Suspense fallback={<PageState tone="loading" title="正在准备工作空间" />}>
      <AuthenticatedWorkspace key={`${session.user.id}:${session.user.contextId ?? ""}`}
        shell={{ pathname:workspacePathname, apiBaseUrl:session.apiBaseUrl ?? apiBaseUrl,
          isDesktopRuntime, user:session.user, onLogout:handleLogout, unitContext, notice:workspaceNotice,
          onDismissNotice:()=>setWorkspaceNotice(null), sessionAttention }}
        routes={{ sessionToken:session.accessToken, activeProduct, canManageAuditLogs, client, routeAccessAllowed, user:session.user }} />
    </Suspense>
  );
}

function readDefaultApiBaseUrl() {
  return import.meta.env.VITE_EXPORTDOC_API_BASE_URL ?? window.location.origin;
}

export default App;
