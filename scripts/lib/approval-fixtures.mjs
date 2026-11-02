// Explicit versioned definitions for end-to-end workflow scenarios.
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';

export async function fulfillNodeInUi(page, note) {
  await page.getByRole('button', { name: '办理此节点', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('实际办理说明', { exact: true }).fill(note);
  await dialog.getByRole('button', { name: '保存办理结果', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });
}

export async function publishDefinitionInUi(page, definition, applicantName, operations, configureSample, appointmentName) {
  await definition.getByRole('button', { name: '仿真并发布此流程', exact: true }).click();
  const simulation = page.getByRole('dialog').filter({ has: page.getByRole('button', { name: '选择人员任职', exact: true }) });
  assert(await simulation.getByRole('button', { name: '保存并发布此流程', exact: true }).isDisabled());
  await simulation.getByRole('button', { name: '选择人员任职', exact: true }).click();
  const picker = page.getByRole('dialog', { name: '选择部门负责人', exact: true });
  await picker.getByRole('textbox', { name: '按姓名或工号搜索负责人' }).fill(applicantName);
  await picker.getByRole('button', { name: '搜索', exact: true }).click();
  const applicants = picker.getByRole('button').filter({ hasText: applicantName });
  await (appointmentName ? applicants.filter({ hasText: appointmentName }) : applicants).click();
  await picker.waitFor({ state: 'hidden' });
  if (configureSample) await configureSample(simulation);
  const simulated = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(operations.get('SimulateOaDefinition').route));
  await simulation.getByRole('button', { name: '解析模拟路线', exact: true }).click();
  const simulationResponse = await simulated;
  assert.equal(simulationResponse.status(), 200, await simulationResponse.text());
  await simulation.getByRole('button', { name: '保留此发布样例', exact: true }).click();
  const published = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(operations.get('PublishOaDefinition').route));
  await simulation.getByRole('button', { name: '保存并发布此流程', exact: true }).click();
  const publication = await published;
  assert.equal(publication.status(), 200, await publication.text());
  await simulation.waitFor({ state: 'hidden' });
  return publication;
}

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

export async function publishDefinitions(invoke, token, settings, keys, samplesByDefinition = {}) {
  let saved = await invoke('SaveOaApprovalSettings', { ...settings, expectedVersion: settings.versionNumber }, {}, token);
  let candidates;
  for (const definitionKey of keys) {
    const definition = saved.rules.find(rule => rule.key === definitionKey);
    let samples = samplesByDefinition[definitionKey];
    if (!samples) {
      const companyCode = (await invoke('getCurrentUser', undefined, {}, token)).companyScope;
      candidates ??= await invoke('ListOrganizationManagers', undefined, { companyCode }, token);
      const failures = [];
      search: for (const person of candidates.items) {
        for (const appointment of person.appointments) {
          const sample = { employeeId: person.id, appointmentId: appointment.id, amount: '0', currency: 'CNY', duration: '1', category: definition.kind === 'leave' ? 'Annual' : 'Other', isReturnable: true };
          try {
            await invoke('SimulateOaDefinition', { definition, ...sample }, {}, token);
            samples = [sample];
            break search;
          } catch (error) { failures.push(error); }
        }
      }
      if (!samples) throw new AggregateError(failures, `No valid publication sample for ${definitionKey}`);
    }
    saved = await invoke('PublishOaDefinition', { expectedVersion: saved.versionNumber, definitionKey, samples, commandKey: randomUUID() }, {}, token);
  }
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
