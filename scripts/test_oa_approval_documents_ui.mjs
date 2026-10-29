import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('oa-approval-documents-ui', async ({ output, url, invoke, openPage }) => {
  const login = await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' });
  const token = login.accessToken;
  const initial = await invoke('GetOaApprovalSettings', undefined, {}, token);
  assert(initial.rules.every(rule => rule.stages[1].source === 'CompanyExecutive'));
  const template = initial.sheetTemplates.find(item => item.key === 'expense');
  const design = JSON.parse(template.content);
  const serialized = JSON.stringify(design);
  assert(serialized.includes('Oa.Approvals'));
  assert(!serialized.includes('FirstStage'));
  assert(!serialized.includes('FollowingStage'));
  function monochrome(value) {
    if (!value || typeof value !== 'object') return;
    for (const [key, child] of Object.entries(value)) {
      if (key === 'backgroundColor') assert(child == null || child === '#ffffff' || child === '', `${key}: ${child}`);
      else if (key === 'color' || key === 'borderColor') assert(child == null || child === '#000000' || child === '', `${key}: ${child}`);
      else monochrome(child);
    }
  }
  monochrome(design);
  assert(!serialized.includes('电子签章'));
  const preview = await invoke('PreviewOaApprovalSheet', template, {}, token);
  assert(preview.html.includes('公司领导审批'));
  assert(preview.html.includes('节点 3'));
  assert(!/会签|或签/u.test(preview.html));
  assert(preview.html.includes('本人未签署'));
  // Converting an existing colored design keeps its fields and geometry.
  const colored = structuredClone(initial);
  const coloredDesign = JSON.parse(colored.sheetTemplates[0].content);
  coloredDesign.layers[0].elements[0].style.color = '#123456';
  coloredDesign.layers[0].elements[0].style.backgroundColor = '#abcdef';
  colored.sheetTemplates[0].content = JSON.stringify(coloredDesign);
  await invoke('SaveOaApprovalSettings', { ...colored, expectedVersion: initial.versionNumber }, {}, token);
  const page = await openPage('oa-review', 'Review-2026-Test');
  await page.goto(`${url}/#/office/approval-settings`);
  const rule = page.locator('details[data-definition-key="expense"]');
  await rule.locator(':scope > summary').click();
  await rule.getByLabel('遇到申请人本人').first().selectOption('Reject');
  await rule.getByLabel('人员兼任').first().selectOption('Reject');
  await rule.getByLabel('通过方式').first().selectOption('Any');
  await rule.getByLabel('审批人来源').first().selectOption('LineManager');
  assert.equal(await rule.getByLabel('通过方式').first().inputValue(), 'Any');
  await rule.getByLabel('审批人来源').first().selectOption('DepartmentManager');
  await page.getByText('审批单名称与打印模板', { exact: true }).click();
  await page.getByRole('button', { name: '使用黑白打印样式', exact: true }).first().click();
  await page.getByRole('button', { name: '编辑审批单模板', exact: true }).first().click();
  const dialog = page.getByRole('dialog', { name: '编辑审批单模板', exact: true });
  await dialog.getByLabel('审批单名称', { exact: true }).fill('费用报销审批记录');
  await dialog.getByRole('button', { name: '预览审批单样例', exact: true }).click();
  const previewDialog = page.getByRole('dialog', { name: '审批单样例预览', exact: true });
  await previewDialog.locator('iframe').waitFor();
  await previewDialog.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await dialog.screenshot({ path: path.join(output, 'sheet-designer.png') });
  await dialog.getByRole('button', { name: '应用到设置草稿', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });
  await page.getByRole('button', { name: '保存审批设置', exact: true }).click();
  await page.getByRole('button', { name: '正在处理…', exact: true }).waitFor({ state: 'hidden' });
  const saved = await invoke('GetOaApprovalSettings', undefined, {}, token);
  assert.equal(saved.sheetTemplates[0].name, '费用报销审批记录');
  assert.equal(JSON.parse(saved.sheetTemplates[0].content).reportType, 'OaApproval');
  monochrome(JSON.parse(saved.sheetTemplates[0].content));
  const savedRule = saved.rules.find(item => item.kind === 'expense');
  assert.equal(savedRule.stages[0].selfApproval, 'Reject');
  assert.equal(savedRule.stages[0].repeatedApprover, 'Reject');
  assert.equal(savedRule.stages[0].decision, 'Any');
  await page.getByRole('button', { name: '全部恢复默认版式', exact: true }).click();
  // Restoration is a settings draft until explicitly saved.
  assert.equal((await invoke('GetOaApprovalSettings', undefined, {}, token)).versionNumber, saved.versionNumber);
  await page.getByRole('button', { name: '保存审批设置', exact: true }).click();
  await page.getByRole('button', { name: '正在处理…', exact: true }).waitFor({ state: 'hidden' });
  const restored = await invoke('GetOaApprovalSettings', undefined, {}, token);
  assert.equal(restored.sheetTemplates[0].name, saved.sheetTemplates[0].name);
  assert(restored.sheetTemplates.every(item => item.content === restored.sheetDefaultContent));
  const invalid = JSON.parse(template.content);
  invalid.layers.find(layer => layer.role === 'Body').visible = false;
  await assert.rejects(() => invoke('PreviewOaApprovalSheet', { ...template, content: JSON.stringify(invalid) }, {}, token), /400/u);
});
