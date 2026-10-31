// Explicit versioned definitions for end-to-end workflow scenarios.
import { randomUUID } from 'node:crypto';

export async function personnelChange(invoke, admin, person, change) {
  const key = randomUUID();
  const user = (await invoke('createUserAccount', { username:`review-${key}`, fullName:'独立人事审批员', role:'OfficeEmployee',
    companyScope:'DEFAULT',departmentId:'GENERAL',isActive:true,resetPassword:'Workflow-UI-2026',
    permissionGrants:['view','approve'].map(action=>({resourceKey:'office.personnel-changes',action,dataScope:'company'}))
  }, {}, admin)).user;
  const reviewer = await invoke('CreatePersonnel', { requestKey:key,employeeNumber:key,departmentId:'GENERAL',jobTitle:'审批员',employmentType:'FullTime',hireDate:'2026-01-01',profile:{fullName:user.fullName} }, {}, admin);
  await invoke('LinkPersonnelAccount', { expectedVersion:reviewer.versionNumber,userId:user.id,expectedAccountVersion:user.versionNumber }, {id:reviewer.employee.id}, admin);
  const token = (await invoke('Login',{username:user.username,password:'Workflow-UI-2026'})).accessToken;
  const settings = await invoke('GetOaApprovalSettings',undefined,{},admin);
  const definition = `personnel:${change.action}`;
  await publishDefinitions(invoke,admin,{...settings,rules:settings.rules.map(rule=>rule.key===definition?{...rule,stages:[approvalStage('personnel-review',[user.id])]}:rule)},[definition]);
  const draft = await invoke('CreatePersonnelChangeRequest',{requestKey:randomUUID(),employeeId:person.employee.id,title:'正式任职变动',reason:'验证批准后生效',personnelChange:{...change,expectedPersonnelVersion:person.versionNumber}}, {}, admin);
  await invoke('SubmitPersonnelChangeRequest',{expectedVersion:draft.versionNumber,note:'提交独立复核'}, {id:draft.id}, admin);
  await approveTask(invoke,token,'PersonnelChange',draft.id);
  return invoke('GetPersonnel',undefined,{id:person.employee.id},admin);
}

export async function linkBusinessPerson(invoke, admin, user) {
  const key=randomUUID();
  const person=await invoke('CreatePersonnel',{requestKey:key,employeeNumber:key,departmentId:user.departmentId,jobTitle:'验收岗位',employmentType:'FullTime',hireDate:'2026-01-01',profile:{fullName:user.fullName}},{},admin);
  return invoke('LinkPersonnelAccount',{expectedVersion:person.versionNumber,userId:user.id,expectedAccountVersion:user.versionNumber},{id:person.employee.id},admin);
}

export async function publishPermissionScheme(invoke, token, scheme) {
  return invoke('PublishPermissionScheme',{schemeId:String(scheme.id),expectedVersion:scheme.versionNumber,note:'发布验收权限版本'}, {}, token);
}
export function approvalStage(key, approverUserIds = [], dependsOn = [], source = 'Named') {
  return { key, name: key, dependsOn, source, approverUserIds, decision: 'All', selfApproval: 'Skip', repeatedApprover: 'Allow', conditions: [], dueHours: 48 };
}

export async function publishDefinitions(invoke, token, settings, keys) {
  let saved = await invoke('SaveOaApprovalSettings', { ...settings, expectedVersion: settings.versionNumber }, {}, token);
  for (const definitionKey of keys) saved = await invoke('PublishOaDefinition', { expectedVersion: saved.versionNumber, definitionKey }, {}, token);
  return saved;
}

export async function approveTask(invoke, token, kind, id, note = '审批验收') {
  const row = await invoke(`Get${kind}Request`, undefined, { id }, token);
  if (!row.reviewTaskKeys?.length) throw new Error(`No assigned task for ${kind} #${id}`);
  return invoke(`Approve${kind}Request`, { expectedVersion: row.versionNumber, taskKey: row.reviewTaskKeys[0], note }, { id }, token);
}


export async function approveResourceTask(invoke, token, kind, title) {
  const room = kind === 'room';
  const page = await invoke(room ? 'ListMeetingBookings' : 'ListOfficeSupplyRequests', undefined, { mineOnly: false, status: 'Pending' }, token);
  const row = page.items.find(row => (room ? row.title : row.supplyName) === title);
  if (!row?.reviewTaskKeys?.length) throw new Error('Missing resource approval task: ' + title);
  return invoke(room ? 'ApproveMeetingBooking' : 'ApproveOfficeSupplyRequest', { expectedVersion: row.versionNumber, taskKey: row.reviewTaskKeys[0], note: '资源独立审批' }, { id: row.id }, token);
}
