import type { ApiUserDto } from "../api/index.ts";
import { Dialog } from "../ui/primitives/Dialog.tsx";
import { CollapsibleSection, StatusBadge } from "../ui/primitives/Card.tsx";
import { InlineNotice } from "../ui/PageState.tsx";
import { formatBusinessDateTime } from "../ui/businessTime.ts";
import { getProductEditionPresentation } from "./productEdition.ts";
import "./permission-source-summary.css";

export function PermissionSourceSummary({ user, onClose, isDesktopRuntime = false }: { user: ApiUserDto; onClose: () => void; isDesktopRuntime?: boolean }) {
  const source = user.permissionSource;
  if (!source) return null;
  const restrictions = ([
    ["账号限制",source.accountDisabledModules], ["身份限制",source.identityDisabledModules],
    ["方案限制",source.schemeDisabledModules], ["上级授权限制",source.authorityDisabledModules],
  ] as const).filter(([, modules]) => modules.length > 0);
  return <Dialog title="当前身份与权限来源" onClose={onClose}>
    <div className="permission-source-summary">
      <dl className="permission-source-facts">
        <div><dt>当前单位</dt><dd>{user.unitContexts?.find(c => c.id === (user.contextId ?? ""))?.companyName ?? user.companyScope}</dd></div>
        <div><dt>产品版本</dt><dd>{getProductEditionPresentation(user.capabilities.productEdition, isDesktopRuntime).editionName}</dd></div>
        <div className="permission-source-fact-wide"><dt>权限来源</dt><dd>{source.name}{source.templateVersion != null && <StatusBadge tone="info">版本 {source.templateVersion}</StatusBadge>}</dd></div>
        <div className="permission-source-fact-wide"><dt>OA 可见范围</dt><dd>{source.oaAssignedOnly ? "仅本人及明确参与的事项" : "按已授权操作的数据范围"}</dd></div>
        {source.startsAt && <div><dt>授权生效</dt><dd>{formatBusinessDateTime(source.startsAt,user.businessTimeZone)}</dd></div>}
        {source.endsAt && <div><dt>授权到期</dt><dd>{formatBusinessDateTime(source.endsAt,user.businessTimeZone)}</dd></div>}
      </dl>
      {restrictions.length > 0 && <InlineNotice tone="warning" title="受限功能">
        <dl className="permission-source-restrictions">{restrictions.map(([label,modules]) => <div key={label}><dt>{label}</dt><dd>{modules.join("、")}</dd></div>)}</dl>
      </InlineNotice>}
      <CollapsibleSection title="已授权操作与数据范围">
        {source.operations.length > 0 ? <ul className="permission-source-operations">{source.operations.map(operation => <li key={operation}>{operation}</li>)}</ul>
          : <p className="permission-source-empty">当前没有已授权操作。</p>}
      </CollapsibleSection>
    </div>
  </Dialog>;
}
