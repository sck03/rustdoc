import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';
import { approvalStage, linkBusinessPerson, openDefinitionInUi } from './lib/approval-fixtures.mjs';

await withOfficeUi('oa-responsibility-ui', async ({ invoke, output, openPage, url }) => {
  const admin = (await invoke('Login', { username:'oa-review', password:'Review-2026-Test' })).accessToken;
  const create = async (username, role, permissionGrants) => (await invoke('createUserAccount', {
    username, fullName:username, role, companyScope:'DEFAULT', departmentId:'GENERAL', isActive:true,
    resetPassword:'Responsibility-2026', ...(permissionGrants ? { permissionGrants } : {}),
  }, {}, admin)).user;
  const applicant = await linkBusinessPerson(invoke, admin, await create('responsibility-applicant', 'OfficeEmployee'));
  const reviewer = await create('responsibility-reviewer', 'CompanyApprover');
  await linkBusinessPerson(invoke, admin, reviewer);
  const resources = ['office.leave','office.overtime','office.expenses','office.travel','office.purchase','office.general','office.rooms','office.supplies'];
  const handler = await create('responsibility-handler', 'OfficeEmployee', resources.flatMap(resourceKey =>
    (['office.rooms','office.supplies'].includes(resourceKey) ? ['view','issue','return'] : ['view','complete'])
      .map(action => ({ resourceKey, action, dataScope:'company' }))));
  const settings = await invoke('GetOaApprovalSettings', undefined, {}, admin);
  const definitions = settings.rules.filter(rule => !rule.form).map(rule => ({ ...rule, stages:[
    approvalStage('independent-review', [reviewer.id]), ...rule.stages.filter(stage => stage.phase === 'Fulfillment')
      .map(stage => ({ ...stage, dependsOn:['independent-review'] })),
  ] }));
  const sample = { employeeId:applicant.employee.id, appointmentId:applicant.organization.primaryAssignmentId,
    amount:'12.35', currency:'CNY', duration:'1', category:'Other', isReturnable:true };
  const report = { missingHandlers:{}, resolved:[], unlinkedAccountExcluded:true };
  for (const definition of definitions) {
    if (definition.kind === 'personnel') {
      await invoke('SimulateOaDefinition', { definition, ...sample }, {}, admin);
    } else {
      await assert.rejects(() => invoke('SimulateOaDefinition', { definition, ...sample }, {}, admin), cause => {
        assert.match(cause.message, /409.*节点“.*查看及 .*未找到启用且关联有效人员任职/u);
        report.missingHandlers[definition.key] = cause.message;
        return true;
      });
    }
  }
  assert.equal(Object.keys(report.missingHandlers).length, 8);
  assert.match(report.missingHandlers.leave, /销假登记/u);
  const leave = definitions.find(rule => rule.key === 'leave');
  await invoke('SaveOaApprovalSettings', { section:'rules', definitionKey:'leave', rule:leave,
    expectedRevision:settings.revisions?.['rule:leave'] ?? 0 }, {}, admin);
  const page = await openPage('oa-review', 'Review-2026-Test');
  await page.goto(`${url}/#/office/approval-settings`);
  const editor = await openDefinitionInUi(page, 'leave', '仿真与发布');
  await editor.getByRole('button', { name:'模拟当前流程', exact:true }).click();
  const simulation = page.getByRole('dialog').filter({ has:page.getByRole('button', { name:'解析模拟路线', exact:true }) });
  await simulation.getByRole('button', { name:'选择人员任职', exact:true }).click();
  const picker = page.getByRole('dialog', { name:'选择部门负责人', exact:true });
  await picker.getByRole('textbox', { name:'按姓名或工号搜索负责人' }).fill('responsibility-applicant');
  await picker.getByRole('button', { name:'搜索', exact:true }).click();
  await picker.getByRole('button').filter({ hasText:'responsibility-applicant' }).click();
  await simulation.getByRole('button', { name:'解析模拟路线', exact:true }).click();
  await simulation.getByText(/节点“.*销假登记.*未找到启用/u).waitFor();
  await page.screenshot({ path:path.join(output, 'missing-handler.png') });
  await linkBusinessPerson(invoke, admin, handler);
  for (const definition of definitions) {
    const result = await invoke('SimulateOaDefinition', { definition, ...sample }, {}, admin);
    for (const node of result.approvalPlan.nodes.filter(node => node.kind === 'Handling')) {
      assert.deepEqual(node.actors.map(actor => actor.userId), [handler.id]);
      assert(node.actors.every(actor => actor.personId && !actor.administration));
    }
    report.resolved.push(definition.key);
  }
  const supply = definitions.find(rule => rule.kind === 'supply');
  const consumed = await invoke('SimulateOaDefinition', { definition:supply, ...sample, isReturnable:false }, {}, admin);
  assert(consumed.approvalPlan.nodes.find(node => node.handlingAction === 'Return').skipped);
  const named = structuredClone(leave);
  const namedHandler = named.stages.find(node => node.kind === 'Handling');
  namedHandler.source = 'Named'; namedHandler.approverUserIds = [reviewer.id];
  await assert.rejects(() => invoke('SimulateOaDefinition', { definition:named, ...sample }, {}, admin), /403.*节点“.*销假登记.*responsibility-reviewer/u);
  await simulation.getByRole('button', { name:'解析模拟路线', exact:true }).click();
  await simulation.getByText('责任人员：responsibility-handler', { exact:true }).first().waitFor();
  assert.equal(await simulation.getByText(/未找到启用/u).count(), 0);
  await page.screenshot({ path:path.join(output, 'resolved-handler.png') });
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(report, null, 2));
});
