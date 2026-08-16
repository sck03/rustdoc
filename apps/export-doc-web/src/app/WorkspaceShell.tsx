import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  ChevronDown,
  ChevronRight,
  FileText,
  LogOut,
  Menu,
  PanelLeftClose,
  PanelLeftOpen,
  RefreshCw,
  Server,
  ServerOff,
  SlidersHorizontal,
  WifiOff,
  X,
} from "lucide-react";
import { Link } from "react-router-dom";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import type { ApiUserDto } from "../api/index.ts";
import {
  createInitialWorkspaceNavGroupState,
  filterWorkspaceNavGroups,
  findActiveWorkspaceNavGroupKey,
  getWorkspaceContext,
  type WorkspaceNavGroupConfig,
} from "./workspaceNavigation.ts";
import { getProductEditionPresentation } from "./productEdition.ts";
import { Button, IconButton } from "../ui/Button.tsx";
import { InlineNotice } from "../ui/PageState.tsx";
import { getServiceConnectionLabel, resolveServiceConnectionState, type ServiceAvailability } from "../ui/serviceAvailabilityModel.ts";
import { useOnlineStatus } from "../ui/useOnlineStatus.ts";
import { useServiceAvailability } from "../ui/useServiceAvailability.ts";
import {
  applyInterfaceDensity,
  persistInterfaceDensity,
  readInterfaceDensity,
  toggleInterfaceDensity,
} from "./interfaceDensity.ts";
import { useWorkspaceDeviceMode } from "./workspaceDevice.ts";

export type WorkspaceNotice = {
  id: "permission" | "license";
  tone: "error" | "warning" | "info";
  title: string;
  message: string;
};

export type WorkspaceSessionAttention = {
  state: "warning" | "expired";
  message: string;
  isBusy: boolean;
  password: string;
  errorMessage?: string | null;
  onPasswordChange: (value: string) => void;
  onContinue: () => void;
  onReauthenticate: () => void;
  onDiscardAndLogout: () => void;
};

type WorkspaceShellProps = {
  pathname: string;
  apiBaseUrl: string;
  isDesktopRuntime: boolean;
  user: ApiUserDto;
  onLogout: () => void;
  children: ReactNode;
  connectivityOverride?: "online" | "offline";
  serviceAvailabilityOverride?: ServiceAvailability;
  notice?: WorkspaceNotice | null;
  onDismissNotice?: () => void;
  sessionAttention?: WorkspaceSessionAttention | null;
};

export function WorkspaceShell({
  pathname,
  apiBaseUrl,
  isDesktopRuntime,
  user,
  onLogout,
  children,
  connectivityOverride,
  serviceAvailabilityOverride,
  notice,
  onDismissNotice,
  sessionAttention,
}: WorkspaceShellProps) {
  const [isNavCollapsed, setIsNavCollapsed] = useState(false);
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false);
  const [interfaceDensity, setInterfaceDensity] = useState(readInterfaceDensity);
  const queryClient = useQueryClient();
  const [latestCacheTimestamp, setLatestCacheTimestamp] = useState(() =>
    readLatestQueryCacheTimestamp(queryClient),
  );
  const mobileNavToggleRef = useRef<HTMLButtonElement | null>(null);
  const mobileNavRef = useRef<HTMLElement | null>(null);
  const workspaceMainRef = useRef<HTMLElement | null>(null);
  const workspaceContentRef = useRef<HTMLDivElement | null>(null);
  const sessionAttentionRef = useRef<HTMLDivElement | null>(null);
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const isOnline = useOnlineStatus(connectivityOverride);
  const { availability: serviceAvailability, retry: retryServiceAvailability } = useServiceAvailability({
    apiBaseUrl,
    enabled: isDesktopRuntime || isOnline,
    override: serviceAvailabilityOverride,
  });
  const visibleGroups = useMemo(
    () => filterWorkspaceNavGroups({ ...user.capabilities, isDesktopRuntime }),
    [isDesktopRuntime, user.capabilities],
  );
  const activeGroupKey = useMemo(
    () => findActiveWorkspaceNavGroupKey(pathname, visibleGroups),
    [pathname, visibleGroups],
  );
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() =>
    createInitialWorkspaceNavGroupState(pathname, visibleGroups),
  );

  useEffect(() => {
    if (isDesktopRuntime || isOnline) {
      return undefined;
    }

    const updateLatestCacheTimestamp = () => {
      setLatestCacheTimestamp(readLatestQueryCacheTimestamp(queryClient));
    };
    updateLatestCacheTimestamp();
    return queryClient.getQueryCache().subscribe(updateLatestCacheTimestamp);
  }, [isDesktopRuntime, isOnline, queryClient]);

  useEffect(() => {
    setExpandedGroups((current) => {
      if (current.has(activeGroupKey)) {
        return current;
      }
      const next = new Set(current);
      next.add(activeGroupKey);
      return next;
    });
  }, [activeGroupKey]);

  useEffect(() => {
    setIsMobileNavOpen(false);
  }, [pathname]);

  useEffect(() => {
    if (!isMobileNavOpen) {
      return undefined;
    }

    const documentElement = document.documentElement;
    const workspaceMain = workspaceMainRef.current;
    const previousOverflow = documentElement.style.overflow;
    const previousMainInert = workspaceMain?.inert ?? false;
    documentElement.style.overflow = "hidden";
    documentElement.dataset.mobileNavigationOpen = "true";
    if (workspaceMain) {
      workspaceMain.inert = true;
    }

    const focusableElements = () => [
      mobileNavToggleRef.current,
      ...Array.from(mobileNavRef.current?.querySelectorAll<HTMLElement>("a[href], button:not(:disabled)") ?? []),
    ].filter((element): element is HTMLElement => Boolean(element));

    const focusInitialNavigationItem = window.requestAnimationFrame(() => {
      const preferredTarget = mobileNavRef.current?.querySelector<HTMLElement>('[aria-current="page"]')
        ?? mobileNavRef.current?.querySelector<HTMLElement>("button, a[href]");
      preferredTarget?.focus();
    });

    const handleNavigationKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        setIsMobileNavOpen(false);
        window.requestAnimationFrame(() => mobileNavToggleRef.current?.focus());
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusable = focusableElements();
      if (focusable.length === 0) {
        return;
      }

      const currentIndex = focusable.indexOf(document.activeElement as HTMLElement);
      if (event.shiftKey && currentIndex <= 0) {
        event.preventDefault();
        focusable[focusable.length - 1]?.focus();
      } else if (!event.shiftKey && currentIndex === focusable.length - 1) {
        event.preventDefault();
        focusable[0]?.focus();
      }
    };

    window.addEventListener("keydown", handleNavigationKeyDown);
    return () => {
      window.cancelAnimationFrame(focusInitialNavigationItem);
      window.removeEventListener("keydown", handleNavigationKeyDown);
      documentElement.style.overflow = previousOverflow;
      delete documentElement.dataset.mobileNavigationOpen;
      if (workspaceMain) {
        workspaceMain.inert = previousMainInert;
      }
    };
  }, [isMobileNavOpen]);

  useEffect(() => {
    if (sessionAttention?.state !== "expired") {
      return undefined;
    }

    const focusFrame = window.requestAnimationFrame(() => sessionAttentionRef.current?.focus());
    return () => window.cancelAnimationFrame(focusFrame);
  }, [sessionAttention?.state]);

  useEffect(() => {
    applyInterfaceDensity(interfaceDensity);
  }, [interfaceDensity]);

  useEffect(() => {
    const compactWorkspace = window.matchMedia("(min-width: 861px) and (max-width: 1180px)");
    const mobileWorkspace = window.matchMedia("(max-width: 860px)");
    const applyWorkspaceWidth = (matches: boolean) => setIsNavCollapsed(matches);
    applyWorkspaceWidth(compactWorkspace.matches);
    const handleChange = (event: MediaQueryListEvent) => applyWorkspaceWidth(event.matches);
    const handleMobileChange = (event: MediaQueryListEvent) => {
      if (!event.matches) setIsMobileNavOpen(false);
    };
    compactWorkspace.addEventListener("change", handleChange);
    mobileWorkspace.addEventListener("change", handleMobileChange);
    return () => {
      compactWorkspace.removeEventListener("change", handleChange);
      mobileWorkspace.removeEventListener("change", handleMobileChange);
    };
  }, []);

  function toggleGroup(groupKey: string) {
    setExpandedGroups((current) => {
      const next = new Set(current);
      if (next.has(groupKey)) {
        next.delete(groupKey);
      } else {
        next.add(groupKey);
      }
      return next;
    });
  }

  function handleToggleInterfaceDensity() {
    const nextDensity = toggleInterfaceDensity(interfaceDensity);
    setInterfaceDensity(nextDensity);
    persistInterfaceDensity(nextDensity);
  }

  const context = getWorkspaceContext(pathname);
  const ContextIcon = context.icon;
  const displayName = user.fullName || user.username;
  const productText = renderProductText(user);
  const showConnectivityNotice = !isDesktopRuntime && !isOnline;
  const serviceConnectionState = resolveServiceConnectionState({ isDesktopRuntime, isOnline, availability: serviceAvailability });
  const showServiceUnavailableNotice = serviceConnectionState === "unreachable";
  const serviceStatusLabel = getServiceConnectionLabel(serviceConnectionState);
  const sessionExpired = sessionAttention?.state === "expired";

  return (
    <div
      className={isNavCollapsed ? "app-shell app-shell-nav-collapsed" : "app-shell"}
      data-workspace-device={workspaceDeviceMode}
    >
      <aside
        className={isMobileNavOpen ? "workspace-nav workspace-nav-mobile-open" : "workspace-nav"}
        inert={sessionExpired}
        aria-disabled={sessionExpired}
      >
        <div className="brand-mark">
          <span className="brand-icon">
            <FileText size={20} aria-hidden="true" />
          </span>
          <span className="brand-copy">
            <strong>{productText.title}</strong>
            <small>{productText.subtitle}</small>
          </span>
        </div>

        <button
          ref={mobileNavToggleRef}
          className="mobile-nav-toggle"
          type="button"
          aria-label={isMobileNavOpen ? "关闭主导航" : "打开主导航"}
          aria-expanded={isMobileNavOpen}
          aria-controls="workspace-primary-navigation"
          onClick={() => setIsMobileNavOpen((current) => !current)}
        >
          {isMobileNavOpen ? <X size={19} aria-hidden="true" /> : <Menu size={19} aria-hidden="true" />}
        </button>

        <div className="workspace-product-badge" role="status" aria-label="产品运行模式">
          <span className="workspace-product-badge-dot" aria-hidden="true" />
          <span>{isDesktopRuntime ? "本机运行" : "多人协作"}</span>
        </div>

        {isNavCollapsed ? (
          <WorkspaceNavRail groups={visibleGroups} pathname={pathname} />
        ) : (
          <nav ref={mobileNavRef} id="workspace-primary-navigation" className="nav-list" aria-label="主导航">
            {visibleGroups.map((group) => (
              <WorkspaceNavGroup
                key={group.key}
                group={group}
                pathname={pathname}
                isExpanded={expandedGroups.has(group.key)}
                isActive={activeGroupKey === group.key}
                onToggle={toggleGroup}
              />
            ))}
          </nav>
        )}

        <div className="workspace-nav-footer">
          <button
            className="nav-collapse-button"
            type="button"
            aria-label={isNavCollapsed ? "展开导航" : "收起导航"}
            title={isNavCollapsed ? "展开导航" : "收起导航，给编辑区更多空间"}
            onClick={() => setIsNavCollapsed((current) => !current)}
          >
            {isNavCollapsed ? <PanelLeftOpen size={17} aria-hidden="true" /> : <PanelLeftClose size={17} aria-hidden="true" />}
            <span>{isNavCollapsed ? "展开" : "收起导航"}</span>
          </button>
        </div>
      </aside>

      {isMobileNavOpen ? (
        <button
          className="workspace-nav-backdrop"
          type="button"
          aria-label="关闭主导航"
          onClick={() => {
            setIsMobileNavOpen(false);
            window.requestAnimationFrame(() => mobileNavToggleRef.current?.focus());
          }}
        />
      ) : null}

      <main ref={workspaceMainRef} className="workspace-main">
        <header className="workspace-header">
          <div className="workspace-title-cluster">
            <span className="workspace-context-icon" aria-hidden="true">
              <ContextIcon size={20} />
            </span>
            <div className="workspace-title-block">
              <p className="eyebrow">{context.section}</p>
              <h1>{context.title}</h1>
              <p className="workspace-description">{context.description}</p>
            </div>
          </div>
          <div className="session-strip">
            <button
              className="density-toggle-button"
              type="button"
              aria-label={`当前为${interfaceDensity === "compact" ? "紧凑" : "舒适"}密度，切换为${interfaceDensity === "compact" ? "舒适" : "紧凑"}密度`}
              title={`切换为${interfaceDensity === "compact" ? "舒适" : "紧凑"}密度`}
              onClick={handleToggleInterfaceDensity}
            >
              <SlidersHorizontal size={16} aria-hidden="true" />
              <span>{interfaceDensity === "compact" ? "紧凑" : "舒适"}</span>
            </button>
            <span className="service-status" data-state={serviceConnectionState} title={serviceStatusLabel}>
              <span className="service-status-dot" aria-hidden="true" />
              <Server size={15} aria-hidden="true" />
              <span className="api-base">{serviceStatusLabel}</span>
            </span>
            <span className="session-user">
              <span className="session-avatar" aria-hidden="true">
                {displayName.trim().slice(0, 1).toUpperCase()}
              </span>
              <span className="session-user-copy">
                <strong>{displayName}</strong>
                <small>{renderUserWorkspaceLabel(user)}</small>
              </span>
            </span>
            <IconButton className="workspace-logout-button" label="退出登录" onClick={onLogout}>
              <LogOut size={18} aria-hidden="true" />
            </IconButton>
          </div>
        </header>

        {showConnectivityNotice ? <div className="workspace-connectivity-notice" role="status" aria-live="polite">
          <WifiOff size={18} aria-hidden="true" />
          <div>
            <strong>设备当前离线</strong>
            <span>{latestCacheTimestamp > 0
              ? `本次会话已加载内容最近更新于 ${formatCacheTimestamp(latestCacheTimestamp)}；联网查询和服务器操作暂时不可用。`
              : "本次会话暂无已加载缓存；联网查询和服务器操作暂时不可用，恢复网络后请重试。"}</span>
          </div>
        </div> : null}

        {showServiceUnavailableNotice ? <div className="workspace-service-notice" role="alert" aria-live="assertive">
          <ServerOff size={18} aria-hidden="true" />
          <div>
            <strong>业务服务暂不可达</strong>
            <span>设备网络可用，但程序无法连接业务服务。已加载内容仍可查看；保存、查询和审核前请先恢复服务。</span>
          </div>
          <Button variant="secondary" icon={<RefreshCw size={16} aria-hidden="true" />} onClick={retryServiceAvailability}>立即重试</Button>
        </div> : null}

        {notice ? <div className="workspace-global-notice">
          <InlineNotice
            tone={notice.tone}
            title={notice.title}
            action={onDismissNotice ? <Button variant="text" onClick={onDismissNotice}>关闭提示</Button> : undefined}
          >
            {notice.message}
          </InlineNotice>
        </div> : null}

        {sessionAttention ? (
          <div
            ref={sessionAttentionRef}
            className="workspace-service-notice"
            role={sessionAttention.state === "expired" ? "alert" : "status"}
            aria-live={sessionAttention.state === "expired" ? "assertive" : "polite"}
            tabIndex={sessionAttention.state === "expired" ? -1 : undefined}
          >
            <div>
              <strong>{sessionAttention.state === "expired" ? "登录已到期，草稿仍保留" : "登录即将到期"}</strong>
              <span>{sessionAttention.message}</span>
              {sessionAttention.errorMessage ? <span className="field-error">{sessionAttention.errorMessage}</span> : null}
            </div>
            {sessionAttention.state === "warning" ? (
              <Button variant="secondary" disabled={sessionAttention.isBusy} onClick={sessionAttention.onContinue}>
                {sessionAttention.isBusy ? "正在续期" : "继续使用"}
              </Button>
            ) : (
              <form className="toolbar" onSubmit={(event) => { event.preventDefault(); sessionAttention.onReauthenticate(); }}>
                <input
                  type="password"
                  value={sessionAttention.password}
                  autoComplete="current-password"
                  placeholder="输入当前账号密码"
                  aria-label="当前账号密码"
                  disabled={sessionAttention.isBusy}
                  onChange={(event) => sessionAttention.onPasswordChange(event.target.value)}
                />
                <Button type="submit" disabled={sessionAttention.isBusy || !sessionAttention.password}>
                  {sessionAttention.isBusy ? "正在验证" : "重新登录并保留草稿"}
                </Button>
                <Button variant="text" type="button" disabled={sessionAttention.isBusy} onClick={sessionAttention.onDiscardAndLogout}>
                  放弃草稿并退出
                </Button>
              </form>
            )}
          </div>
        ) : null}

        <div ref={workspaceContentRef} className="workspace-content" inert={sessionExpired}>{children}</div>
      </main>
    </div>
  );
}

function readLatestQueryCacheTimestamp(queryClient: QueryClient) {
  return queryClient.getQueryCache().getAll().reduce(
    (latest, query) => Math.max(latest, query.state.dataUpdatedAt || 0),
    0,
  );
}

function formatCacheTimestamp(timestamp: number) {
  return new Date(timestamp).toLocaleString("zh-CN", {
    hour12: false,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function renderUserWorkspaceLabel(user: ApiUserDto) {
  if (user.capabilities.canManageSettings) return "管理员";
  if (user.role?.trim().toLowerCase() === "sales") return "业务员";
  if (user.role?.trim().toLowerCase() === "finance") return "财务人员";
  return "单证人员";
}

function renderProductText(user: ApiUserDto) {
  const product = getProductEditionPresentation(user.capabilities.productEdition);
  return { title: product.productName, subtitle: product.editionName };
}

function WorkspaceNavRail({ groups, pathname }: { groups: WorkspaceNavGroupConfig[]; pathname: string }) {
  return (
    <nav className="nav-rail" aria-label="精简主导航">
      {groups.flatMap((group) =>
        group.items.map((item) => {
          const ItemIcon = item.icon;
          const isItemActive = item.isActive(pathname);
          return (
            <Link
              key={`${group.key}-${item.to}`}
              className={isItemActive ? "nav-rail-item nav-rail-item-active" : "nav-rail-item"}
              aria-current={isItemActive ? "page" : undefined}
              title={item.label}
              to={item.to}
            >
              <ItemIcon size={18} aria-hidden="true" />
              <span>{item.label}</span>
            </Link>
          );
        }),
      )}
    </nav>
  );
}

function WorkspaceNavGroup({
  group,
  pathname,
  isExpanded,
  isActive,
  onToggle,
}: {
  group: WorkspaceNavGroupConfig;
  pathname: string;
  isExpanded: boolean;
  isActive: boolean;
  onToggle: (groupKey: string) => void;
}) {
  const GroupIcon = group.icon;
  const ExpandIcon = isExpanded ? ChevronDown : ChevronRight;

  return (
    <section className={isActive ? "nav-group nav-group-active" : "nav-group"}>
      <button className="nav-group-button" type="button" aria-expanded={isExpanded} onClick={() => onToggle(group.key)}>
        <GroupIcon size={17} aria-hidden="true" />
        <span>{group.label}</span>
        <ExpandIcon className="nav-group-chevron" size={16} aria-hidden="true" />
      </button>
      {isExpanded ? (
        <div className="nav-sub-list">
          {group.items.map((item) => {
            const ItemIcon = item.icon;
            const isItemActive = item.isActive(pathname);
            return (
              <Link
                key={item.to}
                className={isItemActive ? "nav-item nav-item-active" : "nav-item"}
                aria-current={isItemActive ? "page" : undefined}
                to={item.to}
              >
                <ItemIcon size={16} aria-hidden="true" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </div>
      ) : null}
    </section>
  );
}
