import { Link, useLocation } from "react-router-dom";
import type { WorkspaceNavGroupConfig } from "./workspaceNavigation.ts";
import { useRouteQuery } from "../ui/useRouteQuery.ts";

export function WorkspaceSectionNavigation({ groups, pathname }: { groups: WorkspaceNavGroupConfig[]; pathname: string }) {
  const location = useLocation();
  const { update } = useRouteQuery();
  const area = groups.flatMap((group) => group.items).find((item) => item.showSectionNav && item.isActive(pathname));
  if (!area?.children) return null;
  const sections = area.children.flatMap((child) => child.searchItems ?? [child]);
  const current = sections.filter((item) => item.isActive(pathname));
  const selected = current.find((item) => !item.search || [...new URLSearchParams(item.search)].every(([key, value]) => new URLSearchParams(location.search).get(key) === value)) ?? current[0];
  return <nav className="workspace-section-nav" aria-label={`${area.label}视图`}>
    {sections.map((item) => {
      const active = selected === item;
      const Icon = item.icon;
      const content = <><Icon size={16} aria-hidden="true" /><span>{item.label}</span></>;
      return item.isActive(pathname)
        ? <button key={item.to} type="button" aria-current={active ? "page" : undefined}
            onClick={() => { if (!active) update(Object.fromEntries(new URLSearchParams(item.search)), false); }}>{content}</button>
        : <Link key={item.to} to={item.to} aria-current={active ? "page" : undefined}>{content}</Link>;
    })}
  </nav>;
}
