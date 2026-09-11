import { useEffect, useRef, useState, type Ref } from "react";
import { ChevronDown, ChevronRight, Search, X } from "lucide-react";
import { Link } from "react-router-dom";
import { searchWorkspaceNavGroups, type WorkspaceNavGroupConfig } from "./workspaceNavigation.ts";

type Props = {
  groups: WorkspaceNavGroupConfig[];
  pathname: string;
  collapsed: boolean;
  activeGroupKey: string;
  expandedGroups: Set<string>;
  navigationRef: Ref<HTMLElement>;
  onToggleGroup: (key: string) => void;
  onExpand: (key?: string) => void;
};

export function WorkspaceNavigation({ groups, pathname, collapsed, activeGroupKey, expandedGroups, navigationRef, onToggleGroup, onExpand }: Props) {
  const [query, setQuery] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);
  useEffect(() => setQuery(""), [pathname]);
  const results = searchWorkspaceNavGroups(query, groups);
  const searching = Boolean(query.trim());

  if (collapsed) return (
    <nav ref={navigationRef} id="workspace-primary-navigation" className="nav-rail" aria-label="精简主导航">
      <button className="nav-rail-item" type="button" aria-label="查找功能" title="查找功能" onClick={() => onExpand()}>
        <Search size={18} aria-hidden="true" /><span>查找</span>
      </button>
      {groups.map((group) => {
        const Icon = group.icon;
        return <button key={group.key} className={group.key === activeGroupKey ? "nav-rail-item nav-rail-item-active" : "nav-rail-item"}
          type="button" aria-label={`展开${group.label}`} aria-current={group.key === activeGroupKey ? "true" : undefined} title={group.label} onClick={() => onExpand(group.key)}>
          <Icon size={18} aria-hidden="true" /><span>{group.shortLabel}</span>
        </button>;
      })}
    </nav>
  );

  return (
    <nav ref={navigationRef} id="workspace-primary-navigation" className="nav-list" aria-label="主导航">
      <form className="nav-search" role="search" aria-label="查找功能" onSubmit={(event) => {
        event.preventDefault();
        Array.from(event.currentTarget.closest("nav")?.querySelectorAll<HTMLAnchorElement>(".nav-item") ?? [])
          .find((link) => link.getClientRects().length)?.focus();
      }}>
        <Search size={16} aria-hidden="true" />
        <input ref={searchRef} type="search" aria-label="查找功能" placeholder="查找功能" maxLength={80} autoComplete="off"
          value={query} onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => { if (event.key === "Escape" && query) { event.preventDefault(); event.stopPropagation(); setQuery(""); } }} />
        {query && <button type="button" aria-label="清除功能搜索" onClick={() => { setQuery(""); searchRef.current?.focus(); }}>
          <X size={16} aria-hidden="true" />
        </button>}
      </form>
      {searching && <p className="nav-search-status" role="status">{results.length ? `找到 ${results.reduce((count, group) => count + group.items.length, 0)} 项功能` : "未找到可用功能，请换个关键词"}</p>}
      {results.map((group) => {
        const Icon = group.icon;
        const expanded = searching || expandedGroups.has(group.key);
        const ExpandIcon = expanded ? ChevronDown : ChevronRight;
        return <section key={group.key} className={activeGroupKey === group.key ? "nav-group nav-group-active" : "nav-group"}>
          {searching ? <p className="nav-search-group">{group.label}</p> : (
            <button className="nav-group-button" type="button" data-nav-group={group.key} aria-expanded={expanded}
              aria-controls={`navigation-${group.key}`} onClick={() => onToggleGroup(group.key)}>
              <Icon size={17} aria-hidden="true" /><span>{group.label}</span><ExpandIcon className="nav-group-chevron" size={16} aria-hidden="true" />
            </button>
          )}
          <div id={`navigation-${group.key}`} className="nav-sub-list" hidden={!expanded}>
            {group.items.map((item) => {
              const ItemIcon = item.icon;
              const active = item.isActive(pathname);
              return <Link key={item.to} className={active ? "nav-item nav-item-active" : "nav-item"} to={item.to}
                aria-current={active ? "page" : undefined} title={item.description} onClick={() => setQuery("")}>
                <ItemIcon size={16} aria-hidden="true" /><span>{item.label}{searching && <small className="nav-search-location">{item.locationLabel}</small>}</span>
              </Link>;
            })}
          </div>
        </section>;
      })}
    </nav>
  );
}
