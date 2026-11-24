import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { withOfficeUi } from './lib/native-office-ui.mjs';
import { approvalStage, approveTask, fulfillNodeInUi, linkBusinessPerson, publishDefinitions } from './lib/approval-fixtures.mjs';

await withOfficeUi('finance-receipt-ui', async ({ output, url, invoke, openPage }) => {
  const admin = (await invoke('Login', { username:'oa-review', password:'Review-2026-Test' })).accessToken;
  const users = [], people = [];
  for (const [username, fullName, role, grants] of [
    ['finance-applicant', '报销申请人', 'OfficeEmployee', undefined],
    ['finance-reviewer', '独立审批人', 'OfficeEmployee', [...['view','approve'].map(action => ({ resourceKey:'office.expenses', action, dataScope:'company' })), {resourceKey:'office.notifications',action:'view',dataScope:'company'}]],
    ['finance-receiver', '0003', 'Finance', undefined],
  ]) {
    const user = (await invoke('createUserAccount', { username, fullName, role, companyScope:'DEFAULT', departmentId:'GENERAL',
      isActive:true, resetPassword:'Finance-Receipt-2026', ...(grants ? { permissionGrants:grants } : {}) }, {}, admin)).user;
    users.push(user); people.push(await linkBusinessPerson(invoke, admin, user));
  }
  const finance = await invoke('Login', { username:'finance-receiver', password:'Finance-Receipt-2026' });
  const permissions = finance.user.capabilities.permissions.filter(grant => grant.resourceKey === 'office.expenses');
  assert(permissions.some(grant => grant.action === 'view' && grant.dataScope === 'company'));
  assert(permissions.some(grant => grant.action === 'complete' && grant.dataScope === 'company'));
  assert(!permissions.some(grant => grant.action === 'approve'));
  // Existing combined schemes retain their published department view and approval scope.
  const catalog = await invoke('ListPermissionTemplates', undefined, {}, admin);
  const department = catalog.templates.find(template => template.code === 'DepartmentApprover');
  const financeGrants = catalog.templates.find(template => template.code === 'Finance').grants;
  const combined = new Map([...financeGrants, ...department.grants].map(grant => [`${grant.resourceKey}/${grant.action}`, grant]));
  const originalFinance = (await invoke('ListUsers', undefined, {}, admin)).users.find(user => user.id === users[2].id);
  await invoke('updateUserAccount', { ...originalFinance, expectedVersion:originalFinance.versionNumber, resetPassword:'', permissionTemplateId:null,
    permissionTemplateVersion:null, permissionGrants:[...combined.values()] }, { id:users[2].id }, admin);
  const settings = await invoke('GetOaApprovalSettings', undefined, {}, admin);
  const definition = settings.rules.find(rule => rule.key === 'expense');
  const fulfillment = definition.stages.filter(stage => stage.phase === 'Fulfillment');
  assert.equal(fulfillment.find(stage => stage.kind === 'Handling').allowAdministration, false);
  definition.stages = [approvalStage('independent-review', [users[1].id]), ...fulfillment.map(stage => ({ ...stage, dependsOn:['independent-review'] }))];
  const candidates = await invoke('ListOrganizationManagers', undefined, { companyCode:'DEFAULT' }, admin);
  const candidate = candidates.items.find(person => person.id === people[0].employee.id);
  const sample = { employeeId:candidate.id, appointmentId:candidate.appointments[0].id, amount:'12.35', currency:'CNY', duration:'1', category:'Other', isReturnable:false };
  const simulation = await invoke('SimulateOaDefinition', { definition, ...sample }, {}, admin);
  const receive = simulation.approvalPlan.nodes.find(node => node.handlingAction === 'Receive');
  assert.deepEqual(receive.actors.map(actor => actor.userId), [users[2].id], 'Only the finance business identity handles receipt');
  // An old explicit takeover flag still must not auto-enrol maintenance accounts.
  const legacy = structuredClone(definition); legacy.stages.find(stage => stage.kind === 'Handling').allowAdministration = true;
  const legacyPlan = await invoke('SimulateOaDefinition', { definition:legacy, ...sample }, {}, admin);
  assert.deepEqual(legacyPlan.approvalPlan.nodes.find(node => node.handlingAction === 'Receive').actors.map(actor => actor.userId), [users[2].id]);
  const named = definition.stages.find(stage => stage.kind === 'Handling');
  named.source = 'Named'; named.approverUserIds = [users[2].id];
  await publishDefinitions(invoke, admin, settings, ['expense'], { expense:[sample] });
  const account = (await invoke('ListUsers', undefined, {}, admin)).users.find(user => user.id === users[2].id);
  await invoke('updateUserAccount', { ...account, expectedVersion:account.versionNumber, resetPassword:'' }, { id:account.id }, admin);
  const applicant = (await invoke('Login', { username:'finance-applicant', password:'Finance-Receipt-2026' })).accessToken;
  const request = await invoke('CreateExpenseRequest', { requestKey:randomUUID(), title:'财务接收范围回归', reason:'其他员工费用报销', currency:'CNY', lines:[{ category:'Other', spentOn:'2026-11-24', description:'交通费', amount:'12.35' }] }, {}, applicant);
  const proof = new FormData(); proof.set('file', new Blob(['%PDF-1.7\n%%EOF'], { type:'application/pdf' }), 'proof.pdf'); proof.set('expectedVersion', String(request.versionNumber));
  const uploaded = await fetch(`${url}/api/office/expense-requests/${request.id}/attachments`, { method:'POST', headers:{ authorization:`Bearer ${applicant}` }, body:proof });
  assert(uploaded.ok, await uploaded.text());
  const latest = await invoke('GetExpenseRequest', undefined, { id:request.id }, applicant);
  await invoke('SubmitExpenseRequest', { expectedVersion:latest.versionNumber, note:'提交报销' }, { id:request.id }, applicant);
  const reviewer = (await invoke('Login', { username:'finance-reviewer', password:'Finance-Receipt-2026' })).accessToken;
  await approveTask(invoke, reviewer, 'Expense', request.id);
  const notifications = await invoke('ListNotifications', undefined, {}, reviewer);
  const notices = notifications.items.filter(notification => notification.requestId === request.id && notification.action === 'submit');
  assert(notices.length > 0);
  assert(notices.every(notification => notification.actionRequired === false && notification.requestStatus === 'Approved'));
  const page = await openPage('finance-receiver', 'Finance-Receipt-2026');
  await page.goto(`${url}/#/office/requests/expense?view=finance&requestId=${request.id}`);
  await page.getByText('财务接收范围回归', { exact:true }).first().waitFor();
  const liveFinance = (await invoke('Login', { username:'finance-receiver', password:'Finance-Receipt-2026' })).accessToken;
  const row = await invoke('GetExpenseRequest', undefined, { id:request.id }, liveFinance);
  const financeNotifications = await invoke('ListNotifications', undefined, {}, liveFinance);
  assert(financeNotifications.items.some(notification => notification.requestId === request.id));
  assert((await invoke('GetNotificationUnreadCount', undefined, {}, liveFinance)).unreadCount > 0);
  assert.equal(row.status, 'Approved');
  assert(row.workflowTasks.some(task => task.nodeKey === named.key && task.canExecute));
  await fulfillNodeInUi(page, '财务接收回归');
  assert.equal((await invoke('GetExpenseRequest', undefined, { id:request.id }, liveFinance)).status, 'HandedOff');
  await page.goto(`${url}/#/office/notifications`);
  await page.getByRole('heading', {name:/^站内通知/u}).waitFor();
  await page.getByRole('link',{name:'查看关联申请',exact:true}).first().waitFor();
  assert.equal(await page.getByText('加载失败',{exact:true}).count(),0);
  await page.getByRole('button',{name:'全部标记已读',exact:true}).click();
  await page.getByRole('heading',{name:'站内通知 · 0 条未读',exact:true}).waitFor();
  assert.equal((await invoke('GetNotificationUnreadCount', undefined, {}, liveFinance)).unreadCount,0);
  assert((await invoke('ListNotifications', undefined, {}, liveFinance)).items.every(notification => notification.status === 'Read'));
  await page.screenshot({ path:path.join(output, 'finance-receipt.png'), fullPage:true });
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ defaultFinanceScope:true, noDefaultApproval:true, administratorExcluded:true, publication:true, crossEmployeeReceiptVisible:true,
    combinedDepartmentApproval:true, financeNotifications:true, unreadCount:true, readAll:true }, null, 2));
});
