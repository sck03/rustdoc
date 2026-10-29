import { useState } from "react";
import type { ApiUserDto } from "../api/index.ts";
import { OfficeDialog } from "../features/office/OfficeUi.tsx";
import { formatBusinessDateTime } from "../ui/businessTime.ts";

export function PermissionSourceSummary({ user }: { user: ApiUserDto }) {
  const [open, setOpen] = useState(false);
  const source = user.permissionSource;
  if (!source) return null;
  const restrictions = [["账号关闭",source.accountDisabledModules],["身份关闭",source.identityDisabledModules],["方案关闭",source.schemeDisabledModules],["上级授权关闭",source.authorityDisabledModules]] as const;
  return <><button type="button" className="command-button secondary" onClick={() => setOpen(true)}>我的权限来源</button>{open && <OfficeDialog title="当前身份与权限来源" onClose={() => setOpen(false)}>
    <dl className="office-facts"><dt>当前单位</dt><dd>{user.unitContexts?.find(c => c.id === (user.contextId ?? ""))?.companyName ?? user.companyScope}</dd><dt>权限来源</dt><dd>{source.name}{source.templateVersion != null ? ` · 版本 ${source.templateVersion}` : ""}</dd><dt>产品版本</dt><dd>{user.capabilities.productEdition}</dd><dt>OA 可见范围</dt><dd>{source.oaAssignedOnly ? "仅本人及明确参与的事项" : "按各操作的数据范围"}</dd>
      {source.startsAt && <><dt>授权生效</dt><dd>{formatBusinessDateTime(source.startsAt,user.businessTimeZone)}</dd></>}{source.endsAt && <><dt>授权到期</dt><dd>{formatBusinessDateTime(source.endsAt,user.businessTimeZone)}</dd></>}
    </dl><p>当前身份的操作不会与其他单位身份合并。菜单和接口共用以下限制，任务审批还须符合任务分派。</p>
    {restrictions.map(([label,modules]) => <p key={label}><strong>{label}：</strong>{modules.length ? modules.join("、") : "无"}</p>)}
    <details><summary>当前已授权操作与数据范围</summary><ul>{source.operations.map(operation => <li key={operation}>{operation}</li>)}</ul></details>
  </OfficeDialog>}</>;
}
