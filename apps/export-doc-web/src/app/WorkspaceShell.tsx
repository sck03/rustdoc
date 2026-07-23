import { useEffect, useMemo, useState, type ReactNode } from "react";
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
}: WorkspaceShellProps) {
  const [isNavCollapsed, setIsNavCollapsed] = useState(false);
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false);
  const [interfaceDensity, setInterfaceDensity] = useState(readInterfaceDensity);
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
    applyInterfaceDensity(interfaceDensity);
  }, [interfaceDensity]);

  useEffect(() => {
    const compactWorkspace = window.matchMedia("(min-width: 861px) and (max-width: 1180px)");
    const applyWorkspaceWidth = (matches: boolean) => setIsNavCollapsed(matches);
    applyWorkspaceWidth(compactWorkspace.matches);
    const handleChange = (event: MediaQueryListEvent) => applyWorkspaceWidth(event.matches);
    compactWorkspace.addEventListener("change", handleChange);
    return () => compactWorkspace.removeEventListener("change", handleChange);
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

  return (
    <div
      className={isNavCollapsed ? "app-shell app-shell-nav-collapsed" : "app-shell"}
      data-workspace-device={workspaceDeviceMode}
    >
      <aside className={isMobileNavOpen ? "workspace-nav workspace-nav-mobile-open" : "workspace-nav"}>
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
          className="mobile-nav-toggle"
          type="button"
          aria-label={isMobileNavOpen ? "关闭主导航" : "打开主导航"}
          aria-expanded={isMobileNavOpen}
          onClick={() => setIsMobileNavOpen((current) => !current)}
        >
          {isMobileNavOpen ? <X size={19} aria-hidden="true" /> : <Menu size={19} aria-hidden="true" />}
        </button>

        <div className="workspace-product-badge" role="status" aria-label="产品运行模式">
          <span className="workspace-product-badge-dot" aria-hidden="true" />
          <span>{isDesktopRuntime ? "本地优先 · 桌面运行" : "局域网 / 容器协同"}</span>
        </div>

        {isNavCollapsed ? (
          <WorkspaceNavRail groups={visibleGroups} pathname={pathname} />
        ) : (
          <nav className="nav-list" aria-label="主导航">
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

      <main className="workspace-main">
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
            <span className="service-status" data-state={serviceConnectionState} title={apiBaseUrl}>
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
            <span>已加载内容仍可查看；联网查询和服务器操作可能暂时不可用，恢复网络后请明确重试。</span>
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

        <div className="workspace-content">{children}</div>
      </main>
    </div>
  );
}

function renderUserWorkspaceLabel(user: ApiUserDto) {
  if (user.capabilities.canManageSettings) return "系统管理员";
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
