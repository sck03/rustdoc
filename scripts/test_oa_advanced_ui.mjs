import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { approvalStage, fulfillNodeInUi, linkBusinessPerson, openDefinitionInUi } from './lib/approval-fixtures.mjs';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('oa-advanced-ui', async ({ output, url, operations, invoke, openPage }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const password = 'Advanced-UI-2026';
  async function employee(name, actions, scope) {
    const user = (await invoke('createUserAccount', {
      username: `advanced-${randomUUID()}`, fullName: name, role: 'OfficeEmployee',
      companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: password,
      permissionGrants: actions.map(action => ({ resourceKey: 'office.general', action, dataScope: scope })),
    }, {}, token)).user;
    const person = await linkBusinessPerson(invoke, token, user);
    return { user, person };
  }
  const applicant = await employee('高级申请人', ['view', 'create', 'edit'], 'own');
  const reviewer = await employee('职责审批人', ['view', 'approve'], 'company');
  const projectReviewer = await employee('项目复核人', ['view', 'approve'], 'company');
  const handler = await employee('实际办理人', ['view', 'complete'], 'company');
  const directory = await invoke('GetOrganizationDirectory', undefined, {}, token);
  const unitId = String(directory.companies.find(unit => unit.code === 'DEFAULT').id);
  const responsibility = await invoke('SaveResponsibility', {
    unitId, code: 'ADV-REVIEW', name: '高级职责审核', workflowKinds: ['general'], isActive: true,
    assignments: [{ appointmentId: reviewer.person.organization.primaryAssignmentId, period: { startsOn: '2026-01-01', endsOn: null } }],
    expectedVersion: 0, note: '明确职责与任职',
  }, {}, token);
  const project = await invoke('SaveProject', {
    unitId, code: 'ADV-PROJECT', name: '有限审批项目', isActive: true,
    ownerAppointmentId: projectReviewer.person.organization.primaryAssignmentId,
    members: [{ appointmentId: applicant.person.organization.primaryAssignmentId, role: 'Member', period: { startsOn: '2026-01-01', endsOn: null } }],
    expectedVersion: 0, note: '明确成员范围',
  }, {}, token);
  const settings = await invoke('GetOaApprovalSettings', undefined, {}, token);
  settings.rules.find(rule => rule.key === 'general').stages = [
    { ...approvalStage('duty', [reviewer.user.id]), name: '职责审核' },
    { ...approvalStage('project', [projectReviewer.user.id], ['duty']), name: '项目复核' },
  ];
  await invoke('SaveOaApprovalSettings', { ...settings, expectedVersion: settings.versionNumber }, {}, token);

  const admin = await openPage('oa-review', 'Review-2026-Test');
  await admin.goto(`${url}/#/office/approval-settings`);
  const definition = await openDefinitionInUi(admin, 'general');
  const first = definition.locator('.oa-stages-editor > fieldset').nth(0);
  const second = definition.locator('.oa-stages-editor > fieldset').nth(1);
  await first.getByLabel('责任人来源').selectOption('Responsibility');
  await first.getByLabel('岗位／职责／项目').selectOption(responsibility.id);
  await second.getByLabel('责任人来源').selectOption('ProjectLeader');
  await second.getByLabel('岗位／职责／项目').selectOption(project.id);
  await first.locator('summary').filter({ hasText: '生效条件' }).click();
  await first.getByLabel('条件组合').selectOption('Any');
  for (const [field, value] of [['Department', 'GENERAL'], ['Category', 'Other']]) {
    await first.getByRole('button', { name: '添加条件', exact: true }).click();
    await first.getByLabel('条件字段').last().selectOption(field);
    await first.getByLabel('条件值', { exact: true }).last().fill(value);
  }
  await first.locator('summary').filter({ hasText: '超时升级' }).click();
  await first.getByRole('button', { name: '添加升级级次', exact: true }).click();
  await first.getByLabel('累计超时小时', { exact: true }).fill('24');
  await first.getByLabel('节点名称', { exact: true }).fill('职责审核已修改');
  await definition.getByRole('button', { name: '撤销', exact: true }).click();
  assert.equal(await first.getByLabel('节点名称', { exact: true }).inputValue(), '职责审核');
  await definition.getByRole('button', { name: '重做', exact: true }).click();
  await definition.getByRole('button', { name: '流程图', exact: true }).click();
  await definition.getByRole('button', { name: '移动 职责审核已修改', exact: true }).focus();
  await admin.keyboard.press('ArrowRight');

  await definition.getByRole('tab', { name: '仿真与发布', exact: true }).click();
  await definition.getByRole('button', { name: '仿真并发布此流程', exact: true }).click();
  const simulation = admin.getByRole('dialog').filter({ has: admin.getByRole('button', { name: '选择人员任职', exact: true }) });
  assert(await simulation.getByRole('button', { name: '保存并发布此流程', exact: true }).isDisabled());
  await simulation.getByRole('button', { name: '选择人员任职', exact: true }).click();
  const picker = admin.getByRole('dialog', { name: '选择部门负责人', exact: true });
  await picker.getByRole('textbox', { name: '按姓名或工号搜索负责人' }).fill('高级申请人');
  await picker.getByRole('button', { name: '搜索', exact: true }).click();
  await picker.getByRole('button').filter({ hasText: '高级申请人' }).click();
  await picker.waitFor({ state: 'hidden' });
  await simulation.getByLabel('类别代码', { exact: true }).fill('Other');
  await simulation.locator('summary').filter({ hasText: '项目审批范围' }).click();
  await simulation.getByLabel('申请项目').selectOption(project.id);
  const simulated = admin.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(operations.get('SimulateOaDefinition').route));
  await simulation.getByRole('button', { name: '解析模拟路线', exact: true }).click();
  assert.equal((await simulated).status(), 200);
  await simulation.getByRole('button', { name: '保留此发布样例', exact: true }).click();
  await admin.screenshot({ path: path.join(output, 'advanced-publication.png'), fullPage: true });
  const published = admin.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(operations.get('PublishOaDefinition').route));
  await simulation.getByRole('button', { name: '保存并发布此流程', exact: true }).click();
  const publication = await published;
  assert.equal(publication.status(), 200, await publication.text());
  assert.equal(publication.request().postDataJSON().samples[0].projectId, project.id);
  await simulation.waitFor({ state: 'hidden' });
  const saved = await invoke('GetOaApprovalSettings', undefined, {}, token);
  const rule = saved.rules.find(rule => rule.key === 'general');
  assert.equal(rule.stages[0].source, 'Responsibility');
  assert.equal(rule.stages[0].conditionLogic, 'Any');
  assert.equal(rule.stages[0].conditions.length, 2);
  assert.equal(rule.stages[0].escalations[0].afterHours, 24);
  assert.equal(rule.stages[1].source, 'ProjectLeader');
  assert(rule.stages.some(stage => stage.phase === 'Fulfillment' && stage.kind === 'Handling'));

  const applicantToken = (await invoke('Login', { username: applicant.user.username, password })).accessToken;
  let request = await invoke('CreateGeneralRequest', {
    requestKey: randomUUID(), title: '高级职责与项目实际审批', reason: '按发布图办理', category: 'Other', projectId: project.id,
  }, {}, applicantToken);
  request = await invoke('SubmitGeneralRequest', { expectedVersion: request.versionNumber, note: '实际提交' }, { id: request.id }, applicantToken);
  const requestUrl = `${url}/#/office/requests/general?requestId=${request.id}`;
  for (const actor of [reviewer, projectReviewer]) {
    const page = await openPage(actor.user.username, password);
    await page.goto(requestUrl);
    await page.getByRole('button', { name: '批准', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('textarea').fill('核对当前职责后真实签署');
    await dialog.getByRole('button', { name: '批准', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
  }
  const handling = await openPage(handler.user.username, password);
  await handling.goto(requestUrl);
  await fulfillNodeInUi(handling, '实际完成并核对办理结果');
  const complete = await invoke('GetGeneralRequest', undefined, { id: request.id }, applicantToken);
  assert.equal(complete.status, 'Completed');
  await handling.screenshot({ path: path.join(output, 'advanced-fulfillment.png'), fullPage: true });
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, requestId: request.id, definitionVersion: complete.approvalPlan.policyVersion, scopes: ['responsibility', 'project-member', 'OR', 'undo-redo', 'graph', 'sample-publication', 'real-approval', 'fulfillment'] }, null, 2));
});
