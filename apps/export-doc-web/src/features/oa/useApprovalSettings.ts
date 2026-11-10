import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings, OaSimulationSample, OaPublishAction, OaScheduleAction, OaActivation } from "../../api/index.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { mergeSavedDraftFields } from "../../ui/recordDraftMerge.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { calendarDateInputs, calendarEditableFields, type CalendarDateInputs, type CalendarDateKey } from "./businessCalendarDraft.ts";
import { acceptApprovalSection, editableFields, sameDraftValue, type ApprovalSettingsField } from "./approvalSettingsMerge.ts";
export type { ApprovalSettingsField } from "./approvalSettingsMerge.ts";

export type ApprovalAccount = { id: number; fullName: string; username: string; departmentId: string; contexts?: { id: string; name: string; departmentId: string }[] };
export function useApprovalSettings(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return useQuery({ queryKey: ["office", "approval-settings", user.id, user.companyScope, user.contextId], enabled: user.capabilities.canManageUsers || user.capabilities.unitManagement?.workflows,
    queryFn: async ({ signal }) => { const [settings, accounts, routing] = await Promise.all([client.getOaApprovalSettings({ signal }), client.getUnitApproverOptions({}, { signal }), client.getOaRoutingUnits({ signal })]); return { settings, accounts: accounts.users, handlers: accounts.handlers, routingUnits: routing.items }; }, refetchOnWindowFocus: false });
}
export function useApprovalSettingsDraft(client: ExportDocManagerApiClient, initial: OaApprovalSettings) {
  const [draft, setDraft] = useState(initial);
  const [baseline, setBaseline] = useState(initial);
  const [calendarInputs, setCalendarInputs] = useState(() => calendarDateInputs(initial.calendar));
  const operation = useOfficeOperation([["office","approval-settings"],["office","oa-applications"],["office","oa","hub"],["unit-workflow-tasks"],["unit-workflow-summary"]],false);
  const formRef = useRef<HTMLFormElement>(null);
  const attempt=useRef<{fingerprint:string;body:OaPublishAction|OaScheduleAction;submitted:OaApprovalSettings}|null>(null);
  const cancelAttempts=useRef(new Map<string,{version:number;key:string}>());
  useEffect(() => { if (operation.error) formRef.current?.querySelectorAll("details").forEach(section => { section.open = true; }); }, [operation.error]);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: editableFields.some(key=>!sameDraftValue(draft[key],baseline[key])) || !sameDraftValue(calendarInputs,calendarDateInputs(baseline.calendar)), message: "审批设置有未保存的修改。" });
  const changeRule = (index: number, change: Partial<OaApprovalSettings["rules"][number]>) => setDraft(value => ({ ...value, rules: value.rules.map((rule, i) => i === index ? { ...rule, ...change } : rule) }));
  const changeCalendarInput = (key: CalendarDateKey, text: string) => {
    setCalendarInputs(value => ({ ...value, [key]: text }));
    setDraft(value => value.calendar ? { ...value, calendar: { ...value.calendar, [key]: text.split(/[\s,;]+/).filter(Boolean) } } : value);
  };
  const accept = (response: OaApprovalSettings, submitted: OaApprovalSettings, { inputs, removedKey, field, definitionKey }: { inputs?: CalendarDateInputs; removedKey?: string; field?: ApprovalSettingsField; definitionKey?: string } = {}) => {
    const saved = acceptApprovalSection(response, submitted, field, definitionKey);
    setBaseline(saved);
    setDraft(current => {
      const merged = mergeSavedDraftFields(current, submitted, saved, editableFields);
      merged.rules = saved.rules.map(rule => {
        const local = current.rules.find(item => item.key === rule.key);
        return local && local !== submitted.rules.find(item => item.key === rule.key) ? local : rule;
      }).concat(current.rules.filter(rule => rule.key !== removedKey && !saved.rules.some(item => item.key === rule.key) && !submitted.rules.some(item => item.key === rule.key)));
      if (current.calendar && submitted.calendar && saved.calendar) merged.calendar = mergeSavedDraftFields(current.calendar, submitted.calendar, saved.calendar, calendarEditableFields);
      return merged;
    });
    if (inputs) {
      const normalized = calendarDateInputs(saved.calendar);
      setCalendarInputs(current => ({ holidays: current.holidays === inputs.holidays ? normalized.holidays : current.holidays,
        additionalWorkingDays: current.additionalWorkingDays === inputs.additionalWorkingDays ? normalized.additionalWorkingDays : current.additionalWorkingDays }));
    }
    return saved;
  };
  const submission = (field: ApprovalSettingsField, definitionKey?: string): OaApprovalSettings => ({ ...baseline,
    [field]: field === "rules" && definitionKey ? [...baseline.rules.filter(rule => rule.key !== definitionKey), draft.rules.find(item => item.key === definitionKey)!] : draft[field] });
  const write = (submitted: OaApprovalSettings, signal: AbortSignal, field: ApprovalSettingsField, definitionKey?: string) => {
    if(field === "rules" && !definitionKey) throw new Error("请选择要保存的流程。");
    const key = field === "rules" ? `rule:${definitionKey}` : field;
    return client.saveOaApprovalSettings({body:{section:field,expectedRevision:submitted.revisions?.[key] ?? 0,
      ...(field === "rules" ? {definitionKey,rule:submitted.rules.find(rule => rule.key === definitionKey)!} : {[field]:submitted[field]})}},{signal});
  };
  const save = (field: ApprovalSettingsField, definitionKey?: string) => {
    const submitted = submission(field, definitionKey), inputs = field === "calendar" ? calendarInputs : undefined;
    return operation.run(signal => write(submitted, signal, field, definitionKey), saved => { accept(saved, submitted, { inputs, field, definitionKey }); });
  };
  const publish = (definitionKey:string,samples:OaSimulationSample[],activateAt:string|undefined,done:()=>void) => operation.run(async signal => {
    const fingerprintFor=(value:OaApprovalSettings)=>JSON.stringify({definitionKey,samples,activateAt,rule:value.rules.find(rule=>rule.key===definitionKey)});
    const fingerprint=fingerprintFor(draft);
    if(attempt.current?.fingerprint!==fingerprint) {
      const submitted=submission("rules",definitionKey);
      const saved=await write(submitted,signal,"rules",definitionKey);
      signal.throwIfAborted();
      const accepted=accept(saved,submitted,{field:"rules",definitionKey});
      attempt.current={fingerprint:fingerprintFor(saved),submitted:accepted,body:{expectedVersion:saved.versionNumber,expectedRevision:saved.revisions?.[`rule:${definitionKey}`]??0,definitionKey,samples,commandKey:createRequestKey(),...(activateAt?{activateAt}:{})}};
    }
    const body=attempt.current.body;
    return "activateAt" in body?client.scheduleOaDefinition({body:body as OaScheduleAction},{signal}):client.publishOaDefinition({body},{signal});
  },saved=>{const submitted=attempt.current!.submitted;attempt.current=null;accept(saved,submitted,{field:"rules",definitionKey});done();});
  const disable = (definitionKey:string) => operation.run(signal=>client.disableOaDefinition({body:{expectedVersion:baseline.versionNumber,expectedRevision:baseline.revisions?.[`rule:${definitionKey}`]??0,definitionKey}},{signal}), saved => { accept(saved,baseline); });
  const remove = (definitionKey:string, done:()=>void) => {
    if(!baseline.rules.some(rule => rule.key === definitionKey)) {setDraft(value=>({...value,rules:value.rules.filter(rule=>rule.key!==definitionKey)}));done();return;}
    const submitted={...baseline,rules:baseline.rules.filter(rule=>rule.key!==definitionKey)};
    return operation.run(signal=>client.saveOaApprovalSettings({body:{section:"rules",definitionKey,remove:true,expectedRevision:baseline.revisions?.[`rule:${definitionKey}`]??0}},{signal}),saved=>{accept(saved,submitted,{removedKey:definitionKey,field:"rules",definitionKey});done();});
  };
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
    await operation.run(signal => client.getOaApprovalSettings({ signal }), saved=>{attempt.current=null;setBaseline(saved);setDraft(saved);setCalendarInputs(calendarDateInputs(saved.calendar));});
  };
  return { draft, setDraft, operation, formRef, changeRule, save, publish, disable, remove, cancelActivation, reload, calendarInputs, changeCalendarInput, changedElsewhere: initial.versionNumber > baseline.versionNumber || Object.entries(initial.revisions ?? {}).some(([key,revision]) => revision > (baseline.revisions?.[key] ?? 0)) };
}
