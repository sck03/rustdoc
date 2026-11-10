import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { approvalStage, linkBusinessPerson, publishDefinitionInUi, fulfillNodeInUi } from './lib/approval-fixtures.mjs';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('oa-custom-ui', async ({ output, url, operations, invoke, openPage, require }) => {
  const presentation = [];
  async function checkPresentation(page, name, selector = '.oa-workspace') {
    await page.evaluate(require('axe-core').source);
    for (const width of [1440, 1024, 390]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1), `${name}: no page overflow at ${width}`);
      const violations = await page.evaluate(async selector => (await window.axe.run(document.querySelector(selector), { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] } })).violations.map(v => ({ id: v.id, targets: v.nodes.map(n => n.target) })), selector);
      assert.deepEqual(violations, [], `${name}: accessibility at ${width}`);
      presentation.push({ name, width, passed: true });
      if (width !== 1024) await page.screenshot({ path: path.join(output, `${name}-${width}.png`), fullPage: true });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
  }
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const password = 'Custom-UI-2026';
  async function employee(name, actions) {
    const user = (await invoke('createUserAccount', {
      username: `custom-${randomUUID()}`, fullName: name, role: 'OfficeEmployee', companyScope: 'DEFAULT',
      departmentId: 'GENERAL', isActive: true, resetPassword: password,
      permissionGrants: actions.map(action => ({ resourceKey: 'office.general', action, dataScope: 'company' })),
    }, {}, token)).user;
    const person = await linkBusinessPerson(invoke, token, user);
    return { user, person };
  }
  const applicant = await employee('样品申请人', ['view', 'create', 'edit']);
  const reviewer = await employee('样品审核人', ['view', 'approve']);
  const handler = await employee('样品办理人', ['view', 'complete']);
  const settings = await invoke('GetOaApprovalSettings', undefined, {}, token);
  const template = settings.rules.find(rule => rule.key === 'general');
  template.stages = [approvalStage('review', [reviewer.user.id])];
  await invoke('SaveOaApprovalSettings', { section: 'rules', definitionKey: 'general', rule: template, expectedRevision: settings.revisions['rule:general'] ?? 0 }, {}, token);
  const admin = await openPage('oa-review', 'Review-2026-Test');
  await admin.goto(`${url}/#/office/approval-settings`);
  await admin.getByRole('button', { name: '新建审批事项', exact: true }).click();
  await admin.getByLabel('流程名称', { exact: true }).fill('样品寄送');
  const definition = admin.locator('section[data-definition-key]');
  await definition.getByRole('tab', { name: '申请表单', exact: true }).click();
  await definition.getByLabel('事项分类', { exact: true }).fill('外贸业务');
  await definition.getByLabel('字段名称', { exact: true }).fill('客户名称');
  await definition.getByLabel('必填', { exact: true }).check();
  await definition.getByRole('button', { name: '添加字段', exact: true }).click();
  await definition.getByLabel('字段名称', { exact: true }).nth(1).fill('运费');
  await definition.getByLabel('字段类型', { exact: true }).nth(1).selectOption('Money');
  await definition.getByLabel('必填', { exact: true }).nth(1).check();
  await definition.getByRole('button', { name: '添加字段', exact: true }).click();
  await definition.getByLabel('字段名称', { exact: true }).nth(2).fill('收件部门');
  await definition.getByLabel('字段类型', { exact: true }).nth(2).selectOption('Department');
  await definition.getByRole('button', { name: '添加字段', exact: true }).click();
  await definition.getByLabel('字段名称', { exact: true }).nth(3).fill('样品明细');
  await definition.getByLabel('字段类型', { exact: true }).nth(3).selectOption('Table');
  await definition.getByLabel('字段名称', { exact: true }).nth(4).fill('品名');
  await definition.getByRole('button', { name: '添加明细列', exact: true }).click();
  await definition.getByLabel('字段名称', { exact: true }).nth(5).fill('数量');
  await definition.getByLabel('字段类型', { exact: true }).nth(5).selectOption('Decimal');
  await checkPresentation(admin, 'custom-designer');
  const key = await definition.getAttribute('data-definition-key');
  async function sampleLine(scope) {
    await scope.getByRole('button', { name: '添加明细行', exact: true }).click();
    await scope.getByLabel('品名', { exact: true }).fill('展会样品 A');
    await scope.getByLabel('数量', { exact: true }).fill('2.5');
  }
  await publishDefinitionInUi(admin, definition, '样品申请人', operations, async dialog => {
    await dialog.getByLabel('客户名称', { exact: true }).fill('仿真客户');
    await dialog.getByLabel('运费（CNY）', { exact: true }).fill('25.50');
    await dialog.getByLabel('类别代码', { exact: true }).fill('Other');
    await sampleLine(dialog);
  });
  const staff = await openPage(applicant.user.username, password);
  await staff.goto(`${url}/#/office/approvals?view=start`);
  const card = staff.locator('article').filter({ has: staff.getByRole('heading', { name: '样品寄送', exact: true }) });
  await card.getByRole('link', { name: '发起申请', exact: true }).click();
  const dialog = staff.getByRole('dialog', { name: '新建样品寄送', exact: true });
  await dialog.getByLabel('申请标题', { exact: true }).fill('展会样品寄送');
  await dialog.getByLabel('申请说明', { exact: true }).fill('客户确认后寄送样品');
  await dialog.getByLabel('客户名称', { exact: true }).fill('中文客户甲');
  await dialog.getByLabel('运费（CNY）', { exact: true }).fill('25.50');
  await sampleLine(dialog);
  await dialog.getByRole('button', { name: '选择部门', exact: true }).click();
  await checkPresentation(staff, 'custom-form-picker', '[role=dialog]');
  await dialog.getByRole('region', { name: '收件部门选项', exact: true }).getByRole('button').filter({ hasText: '（GENERAL）' }).click();
  const created = staff.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(operations.get('CreateGeneralRequest').route));
  await dialog.getByRole('button', { name: '保存草稿', exact: true }).click();
  const response = await created; assert.equal(response.status(), 200, await response.text());
  const row = await response.json();
  assert.equal(row.definitionKey, key);
  await dialog.waitFor({ state: 'hidden' });
  await staff.getByRole('button', { name: '提交审批', exact: true }).click();
  const submit = staff.getByRole('dialog');
  await submit.locator('textarea').fill('请审核样品');
  await submit.getByRole('button', { name: '提交审批', exact: true }).click();
  await submit.waitFor({ state: 'hidden' });
  const review = await openPage(reviewer.user.username, password);
  const requestUrl = `${url}/#/office/requests/general?requestId=${row.id}`;
  await review.goto(requestUrl);
  await review.getByText('中文客户甲', { exact: true }).waitFor();
  await review.getByText('展会样品 A', { exact: true }).waitFor();
  await review.getByRole('button', { name: '批准', exact: true }).click();
  await review.getByRole('dialog').locator('textarea').fill('样品与运费核对通过');
  await review.getByRole('dialog').getByRole('button', { name: '批准', exact: true }).click();
  await review.getByRole('dialog').waitFor({ state: 'hidden' });
  const handling = await openPage(handler.user.username, password);
  await handling.goto(requestUrl);
  await fulfillNodeInUi(handling, '样品已交付快递');
  const complete = await invoke('GetGeneralRequest', undefined, { id: row.id }, token);
  assert.equal(complete.status, 'Completed');
  assert.equal(complete.applicationName, '样品寄送');
  assert(Object.values(complete.formDisplayValues).some(value => typeof value === 'string' && value.includes('（GENERAL）')));
  await handling.getByRole('button', { name: '下载审批单 PDF', exact: true }).waitFor({ timeout: 65000 });
  const download = handling.waitForEvent('download');
  await handling.getByRole('button', { name: '下载审批单 PDF', exact: true }).click();
  const pdf = path.join(output, 'custom-approval.pdf');
  await (await download).saveAs(pdf);
  assert.equal(fs.readFileSync(pdf).subarray(0, 5).toString(), '%PDF-');
  await handling.screenshot({ path: path.join(output, 'custom-completed.png'), fullPage: true });
  await checkPresentation(handling, 'custom-completed');
  // Deleting one saved definition must preserve unsaved edits in another definition.
  await admin.goto(`${url}/#/office/approval-settings?definition=${encodeURIComponent(key)}&tab=basic`);
  await definition.getByLabel('流程名称', { exact: true }).fill('待保存的样品流程');
  const concurrent = await invoke('GetOaApprovalSettings', undefined, {}, token);
  const concurrentRule = { ...concurrent.rules.find(rule => rule.key === key), name: '另一管理员保存的样品流程' };
  await invoke('SaveOaApprovalSettings', { section: 'rules', definitionKey: key, rule: concurrentRule, expectedRevision: concurrent.revisions[`rule:${key}`] }, {}, token);
  await definition.getByRole('button', { name: '返回定义列表', exact: true }).click();
  await admin.getByRole('button', { name: '新建审批事项', exact: true }).click();
  const disposable = await definition.getAttribute('data-definition-key');
  const savedDraft = admin.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(operations.get('SaveOaApprovalSettings').route));
  await admin.getByRole('button', { name: '保存当前流程', exact: true }).click();
  assert.equal((await savedDraft).status(), 200);
  const removed = admin.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(operations.get('SaveOaApprovalSettings').route));
  await admin.getByRole('button', { name: '删除未发布草稿', exact: true }).click();
  assert.equal((await removed).status(), 200);
  await admin.locator(`tr[data-definition-key="${key}"]`).getByRole('button', { name: '编辑流程', exact: true }).click();
  assert.equal(await definition.getByLabel('流程名称', { exact: true }).inputValue(), '待保存的样品流程');
  const conflict = admin.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(operations.get('SaveOaApprovalSettings').route));
  await admin.getByRole('button', { name: '保存当前流程', exact: true }).click();
  assert.equal((await conflict).status(), 409, 'Saving/deleting another definition must not advance this draft revision');
  assert.equal((await invoke('GetOaApprovalSettings', undefined, {}, token)).rules.find(rule => rule.key === key).name, concurrentRule.name);
  assert(!(await invoke('GetOaApprovalSettings', undefined, {}, token)).rules.some(rule => rule.key === disposable));
  // A limited reviewer must receive neither raw values nor frozen display names.
  let latest = await invoke('GetOaApprovalSettings', undefined, {}, token);
  const limitedRule = latest.rules.find(rule => rule.key === key);
  const departmentKey = limitedRule.form.fields.find(field => field.kind === 'Department').key;
  limitedRule.stages[0].materialPolicy = { hiddenFields: [`formValues.${departmentKey}`] };
  latest = await invoke('SaveOaApprovalSettings', { section: 'rules', definitionKey: key, rule: limitedRule, expectedRevision: latest.revisions[`rule:${key}`] }, {}, token);
  const samples = [{ employeeId: applicant.person.employee.id, appointmentId: applicant.person.organization.primaryAssignmentId, category: 'Other', formValues: complete.formValues }];
  await invoke('PublishOaDefinition', { definitionKey: key, expectedVersion: latest.versionNumber, expectedRevision: latest.revisions[`rule:${key}`], samples }, {}, token);
  const active = (await invoke('ListOaApplications', undefined, {}, token)).items.find(item => item.key === key);
  let limited = await invoke('CreateGeneralRequest', { requestKey: randomUUID(), employeeId: applicant.person.employee.id, title: '受限自定义材料', reason: '核验显示值过滤', category: 'Other', definitionKey: key, definitionVersion: active.version, formValues: complete.formValues }, {}, token);
  limited = await invoke('SubmitGeneralRequest', { expectedVersion: limited.versionNumber }, { id: limited.id }, token);
  const reviewerToken = (await invoke('Login', { username: reviewer.user.username, password })).accessToken;
  limited = await invoke('GetGeneralRequest', undefined, { id: limited.id }, reviewerToken);
  assert.equal(limited.materialAccess.isPartial, true);
  assert.equal(limited.formValues[departmentKey], undefined);
  assert.equal(limited.formDisplayValues[departmentKey], undefined);
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, key, id: row.id, status: complete.status, presentation }, null, 2));
});
