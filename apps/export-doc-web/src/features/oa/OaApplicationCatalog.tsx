import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowRight, ClipboardList } from "lucide-react";
import type { ApiUserDto } from "../../api/index.ts";
import { ListWorkspaceHeader } from "../../ui/ListWorkspaceHeader.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { oaAccess, oaKinds, oaModules, type OaKind } from "./oaModel.ts";

const groups: Record<OaKind, string> = {
  personnel: "人事与考勤", leave: "人事与考勤", overtime: "人事与考勤",
  expense: "费用与出行", travel: "费用与出行", purchase: "行政与资源", general: "行政与资源",
};

export function OaApplicationCatalog({ user, resources }: {
  user: ApiUserDto; resources: readonly { kind: string; title: string; description: string }[];
}) {
  const [search, setSearch] = useState("");
  const [group, setGroup] = useState("");
  const entries = [
    ...oaKinds.filter(kind => oaAccess(user, kind, "view")).map(kind => ({
      key: kind, title: oaModules[kind].name, description: oaModules[kind].description, group: groups[kind],
      create: oaAccess(user, kind, "create") ? `/office/requests/${kind}?new=true` : null,
      href: `/office/requests/${kind}?view=requests`, resource: false,
    })),
    ...resources.map(item => ({ ...item, key: item.kind, group: "行政与资源", create: null,
      href: item.kind === "rooms" ? "/office/meeting-rooms" : "/office/supplies", resource: true })),
  ];
  const availableGroups = [...new Set(entries.map(item => item.group))];
  const keyword = search.trim().toLocaleLowerCase();
  const visible = entries.filter(item => (!group || item.group === group)
    && (!keyword || `${item.title} ${item.description}`.toLocaleLowerCase().includes(keyword)));
  return <div className="oa-application-catalog">
    <ListWorkspaceHeader title="选择申请事项" count={visible.length} unit="项">
      <div className="list-workspace-filters" role="search" aria-label="查找申请事项">
        <input type="search" aria-label="查找申请类型" placeholder="查找请假、报销、用印、会议室…" maxLength={100} value={search} onChange={event => setSearch(event.target.value)} />
        <select aria-label="申请分类" value={group} onChange={event => setGroup(event.target.value)}><option value="">全部分类</option>{availableGroups.map(name => <option key={name}>{name}</option>)}</select>
      </div>
    </ListWorkspaceHeader>
    {availableGroups.map(name => {
      const items = visible.filter(item => item.group === name);
      return items.length > 0 && <section className="oa-application-group" aria-label={name} key={name}>
        <h3>{name}</h3><div className="oa-hub-start-grid">{items.map(item => <article className="oa-hub-start-card" key={item.key}>
          <ClipboardList size={22} aria-hidden="true" /><h4>{item.title}</h4><p className="office-muted">{item.description}</p>
          <div className="office-card-actions">{item.create && <Link className="command-button" to={item.create}>发起申请</Link>}
            <Link className={item.resource ? "command-button secondary" : undefined} to={item.href}>{item.resource ? "选择资源" : "申请记录"}<ArrowRight size={15} aria-hidden="true" /></Link></div>
        </article>)}</div>
      </section>;
    })}
    {!visible.length && <PageState tone="empty" title="没有匹配的申请事项" description="调整关键词或申请分类后重试。" />}
  </div>;
}
