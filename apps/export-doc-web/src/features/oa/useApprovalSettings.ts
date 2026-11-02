import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings, OaSimulationSample, OaPublishAction, OaScheduleAction, OaActivation } from "../../api/index.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { mergeSavedDraftFields } from "../../ui/recordDraftMerge.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

const editableFields = ["rules","delegations","handlingServices","sheetTemplates","calendar"] as const;
function sameDraftValue(a:unknown,b:unknown):boolean {
  if(Object.is(a,b)) return true;
  if(!a || !b || typeof a!=="object" || typeof b!=="object") return false;
  if(Array.isArray(a)) return Array.isArray(b) && a.length===b.length && a.every((value,index)=>sameDraftValue(value,b[index]));
  if(Array.isArray(b)) return false;
  const left=a as Record<string,unknown>, right=b as Record<string,unknown>;
  return Object.keys(left).length===Object.keys(right).length && Object.keys(left).every(key=>Object.hasOwn(right,key) && sameDraftValue(left[key],right[key]));
}

export type ApprovalAccount = { id: number; fullName: string; username: string; departmentId: string; contexts?: { id: string; name: string; departmentId: string }[] };
export function useApprovalSettings(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return useQuery({ queryKey: ["office", "approval-settings", user.id, user.companyScope, user.contextId], enabled: user.capabilities.canManageUsers || user.capabilities.unitManagement?.workflows,
    queryFn: async ({ signal }) => { const [settings, accounts, routing] = await Promise.all([client.getOaApprovalSettings({ signal }), client.getUnitApproverOptions({}, { signal }), client.getOaRoutingUnits({ signal })]); return { settings, accounts: accounts.users, handlers: accounts.handlers, routingUnits: routing.items }; }, refetchOnWindowFocus: false });
}
export function useApprovalSettingsDraft(client: ExportDocManagerApiClient, initial: OaApprovalSettings) {
  const [draft, setDraft] = useState(initial);
  const [baseline, setBaseline] = useState(initial);
  const [calendarReset, resetCalendar] = useState(0);
  const operation = useOfficeOperation([["office","approval-settings"],["office","oa","hub"],["unit-workflow-tasks"],["unit-workflow-summary"]],false);
  const formRef = useRef<HTMLFormElement>(null);
  const attempt=useRef<{fingerprint:string;body:OaPublishAction|OaScheduleAction}|null>(null);
  const cancelAttempts=useRef(new Map<string,{version:number;key:string}>());
  useEffect(() => { if (operation.error) formRef.current?.querySelectorAll("details").forEach(section => { section.open = true; }); }, [operation.error]);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: editableFields.some(key=>!sameDraftValue(draft[key],baseline[key])), message: "审批规则或代理有未保存的修改。" });
  const changeRule = (index: number, change: Partial<OaApprovalSettings["rules"][number]>) => setDraft(value => ({ ...value, rules: value.rules.map((rule, i) => i === index ? { ...rule, ...change } : rule) }));
  const changeDelegate = (index: number, change: Partial<OaApprovalSettings["delegations"][number]>) => setDraft(value => ({ ...value, delegations: value.delegations.map((item, i) => i === index ? { ...item, ...change } : item) }));
  const accept = (saved: OaApprovalSettings,submitted:OaApprovalSettings) => { setBaseline(saved); setDraft(current=>mergeSavedDraftFields(current,submitted,saved,editableFields)); resetCalendar(value=>value+1); };
  const save = () => operation.run(signal => client.saveOaApprovalSettings({ body: { expectedVersion: baseline.versionNumber, rules: draft.rules, delegations: draft.delegations, handlingServices: draft.handlingServices ?? [], sheetTemplates: draft.sheetTemplates, calendar:draft.calendar } }, { signal }), saved=>accept(saved,draft));
  const publish = (definitionKey:string,samples:OaSimulationSample[],activateAt:string|undefined,done:()=>void) => operation.run(async signal => {
    const fingerprintFor=(value:OaApprovalSettings)=>JSON.stringify({definitionKey,samples,activateAt,fields:editableFields.map(key=>value[key])});
    const fingerprint=fingerprintFor(draft);
    if(attempt.current?.fingerprint!==fingerprint) {
      const saved=await client.saveOaApprovalSettings({body:{expectedVersion:baseline.versionNumber,rules:draft.rules,delegations:draft.delegations,handlingServices:draft.handlingServices??[],sheetTemplates:draft.sheetTemplates,calendar:draft.calendar}},{signal});
      accept(saved,draft);
      attempt.current={fingerprint:fingerprintFor(saved),body:{expectedVersion:saved.versionNumber,definitionKey,samples,commandKey:createRequestKey(),...(activateAt?{activateAt}:{})}};
    }
    const body=attempt.current.body;
    return "activateAt" in body?client.scheduleOaDefinition({body:body as OaScheduleAction},{signal}):client.publishOaDefinition({body},{signal});
  },saved=>{attempt.current=null;accept(saved,draft);done();});
  const disable = (definitionKey:string) => operation.run(signal=>client.disableOaDefinition({body:{expectedVersion:baseline.versionNumber,definitionKey}},{signal}), saved => {
    setBaseline(saved);
    setDraft(value => ({ ...value, publications: saved.publications, versionNumber: saved.versionNumber }));
  });
  const cancelActivation=(entry:OaActivation)=> {
    let attempt=cancelAttempts.current.get(entry.id);
    if(!attempt||attempt.version!==entry.versionNumber) {
      attempt={version:entry.versionNumber,key:createRequestKey()};
      cancelAttempts.current.set(entry.id,attempt);
    }
    const commandKey=attempt.key;
    return operation.run(signal=>client.cancelOaActivation({activationId:entry.id,body:{expectedVersion:entry.versionNumber,definitionKey:entry.definitionKey,commandKey}},{signal}),saved=> {
      cancelAttempts.current.delete(entry.id);
      setDraft(value=>({...value,activations:saved.activations}));
    });
  };
  const reload = async () => {
    if (operation.busy || !await confirmDiscardChanges("重新载入审批设置")) return;
    await operation.run(signal => client.getOaApprovalSettings({ signal }), saved=>{attempt.current=null;setBaseline(saved);setDraft(saved);resetCalendar(value=>value+1);});
  };
  return { draft, setDraft, operation, formRef, changeRule, changeDelegate, save, publish, disable, cancelActivation, reload, calendarReset, changedElsewhere: initial.versionNumber > baseline.versionNumber };
}
