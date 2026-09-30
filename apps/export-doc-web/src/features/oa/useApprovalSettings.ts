import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings } from "../../api/index.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";

export type ApprovalAccount = { id: number; fullName: string; username: string; departmentId: string };
export function useApprovalSettings(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return useQuery({ queryKey: ["office", "approval-settings", user.id, user.companyScope], enabled: user.capabilities.canManageUsers,
    queryFn: async ({ signal }) => { const [settings, accounts] = await Promise.all([client.getOaApprovalSettings({ signal }), client.listUsers({ signal })]); return { settings, accounts: accounts.users.filter(account => account.companyScope === user.companyScope && account.isActive) }; }, refetchOnWindowFocus: false });
}
export function useApprovalSettingsDraft(client: ExportDocManagerApiClient, initial: OaApprovalSettings) {
  const [draft, setDraft] = useState(initial);
  const operation = useOfficeOperation();
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => { if (operation.error) formRef.current?.querySelectorAll("details").forEach(section => { section.open = true; }); }, [operation.error]);
  useUnsavedChangesGuard({ isDirty: JSON.stringify(draft) !== JSON.stringify(initial), message: "审批规则或代理有未保存的修改。" });
  const changeRule = (index: number, change: Partial<OaApprovalSettings["rules"][number]>) => setDraft(value => ({ ...value, rules: value.rules.map((rule, i) => i === index ? { ...rule, ...change } : rule) }));
  const changeDelegate = (index: number, change: Partial<OaApprovalSettings["delegations"][number]>) => setDraft(value => ({ ...value, delegations: value.delegations.map((item, i) => i === index ? { ...item, ...change } : item) }));
  const save = () => operation.run(signal => client.saveOaApprovalSettings({ body: { expectedVersion: initial.versionNumber, rules: draft.rules, delegations: draft.delegations } }, { signal }), () => {});
  return { draft, setDraft, operation, formRef, changeRule, changeDelegate, save };
}
