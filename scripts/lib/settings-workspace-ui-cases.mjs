import assert from "node:assert/strict";
import path from "node:path";

export async function runSettingsWorkspaceCases({ open, currentPage, read, click, clickText, input, field, waitFor, audit, captureScreenshot, output, results }) {
  let page;
  const start = async (extra = "", width = 1024) => { await open("settings", width, extra); page = currentPage(); };
  const category = async (label) => {
    await clickText(page, label, ".settings-category-nav button span");
    await waitFor(page, "!document.querySelector('[aria-busy=true]')");
  };
  const textField = async (label, value) => {
    const selector = await read(page, `(()=>{const node=[...document.querySelectorAll('label')].find(n=>n.querySelector(':scope>span')?.textContent===${JSON.stringify(label)})?.querySelector('input,select');if(!node)throw new Error('Missing '+${JSON.stringify(label)});node.dataset.settingsField='current';return '[data-settings-field=current]'})()`);
    await input(page, selector, value);
    await read(page, "document.querySelector('[data-settings-field=current]').removeAttribute('data-settings-field')");
  };
  const fields = async (group) => {
    await clickText(page, "字段名称"); await field(page, "字段分组", group);
    assert.equal(await read(page, "document.querySelectorAll('[aria-label=单据字段名称] input').length"), 10);
  };
  const save = async () => {
    const count = await read(page, "window.__calls.filter(c=>c.name==='updateSettings').length");
    await clickText(page, "保存全部修改");
    await waitFor(page, `window.__calls.filter(c=>c.name==='updateSettings').length>${count} && document.querySelector('.settings-command-heading').textContent.includes('所有修改已保存')`);
    return read(page, "window.__calls.filter(c=>c.name==='updateSettings').at(-1).input.body");
  };

  await start();
  assert.equal(await read(page, "document.querySelector('[aria-label=单据字段名称]')"), null);
  await field(page, "默认显示备用列数", "4");
  for (const group of ["invoice", "item", "payment"]) {
    await fields(group); await input(page, '[aria-label="单据字段名称"] input', `${group}名称`);
  }
  await category("邮件设置"); await textField("SMTP 服务器", "smtp.example.test");
  await click(page, '.settings-command-actions input[type="checkbox"]');
  await textField("邮箱密码", "fixture-password");
  await category("备份与恢复"); await clickText(page, "异机副本", ".settings-section-nav button");
  await textField("副本存储", "WebDav");
  await textField("WebDAV 地址", "https://backup.example.test/dav");
  await textField("WebDAV 用户名", "test-user");
  await clickText(page, "保存灾备设置");
  await waitFor(page, "window.__calls.some(c=>c.name==='saveBackupPlan') && !document.querySelector('.backup-plan-toolbar').textContent.includes('有未保存')");
  await category("维护工具"); await waitFor(page, "document.querySelector('[aria-label=日志管理]')");
  await field(page, "日志保留天数", "60");
  assert(await read(page, "[...document.querySelectorAll('button')].find(n=>n.textContent.trim()==='清理旧日志').disabled"));
  assert.equal(await read(page, "window.__calls.filter(c=>c.name==='cleanupSystemLogs').length"), 0);
  assert(await read(page, "['单据设置','邮件设置','维护工具'].every(label=>document.querySelector('.settings-command-heading').textContent.includes(label))"));
  await category("单据设置"); await fields("invoice");
  assert.equal(await read(page, "document.querySelector('[aria-label=单据字段名称] input').value"), "invoice名称");
  assert.equal(await read(page, "document.querySelector('.settings-command-actions input[type=checkbox]')"), null);
  const saved = await save();
  assert.equal(saved.settings.system.itemEntrySpareColumnCount, 4);
  assert.deepEqual(saved.settings.system.documentFieldLabels, { invoice: { spare1: "invoice名称" }, item: { spare1: "item名称" }, payment: { spare1: "payment名称" } });
  assert.equal(saved.settings.email.smtpHost, "smtp.example.test"); assert.equal(saved.updateSecrets, true);
  assert.equal(saved.settings.email.password, "fixture-password"); assert.equal(saved.settings.webDav.url, "https://backup.example.test/dav");
  assert.equal(saved.settings.system.logRetentionDays, 60);
  await input(page, '[aria-label="单据字段名称"] input', "暂改");
  await input(page, '[aria-label="单据字段名称"] input', "invoice名称");
  assert(await read(page, "document.querySelector('.settings-command-heading').textContent.includes('所有修改已保存')"));
  await category("维护工具"); await clickText(page, "清理旧日志");
  await waitFor(page, "window.__calls.some(c=>c.name==='cleanupSystemLogs')");
  results.push("settings-cross-category-save-groups-secrets-and-log-cleanup");
  await read(page, "[...document.querySelectorAll('.maintenance-section-nav button')].find(n=>n.querySelector('strong')?.textContent==='发票清理').click()");
  await waitFor(page, "window.__route.includes('section=invoice-cleanup') && document.querySelector('[aria-label=发票数据清理]')");
  await category("运行与数据库"); await start("&section=invoice-cleanup");
  await waitFor(page, "document.querySelector('[aria-label=发票数据清理]')");
  await audit(page, "settings-maintenance-route-and-deep-link");

  await start("&section=database&desktop");
  assert(await read(page, "document.querySelector('[aria-label=数据库连接]').textContent.includes('SQLite 文件名')"));
  assert.equal(await read(page, "[...document.querySelectorAll('[aria-label=数据库连接] label > span')].some(n=>n.textContent==='服务器')"), false);
  assert(await read(page, "Boolean(document.querySelector('[aria-label=软件更新]'))"));
  await category("备份与恢复");
  assert.equal(await read(page, "document.querySelector('.settings-section-nav').textContent.includes('团队库')"), false);
  await clickText(page, "恢复与校验", ".settings-section-nav button");
  await waitFor(page, "window.__calls.some(c=>c.name==='listDatabaseBackups')");
  await audit(page, "settings-sqlite-backup-tools");

  await start("&section=database&provider=PostgreSQL");
  assert.equal(await read(page, "document.querySelector('[aria-label=软件更新]')"), null);
  assert.equal(await read(page, "document.querySelector('[aria-label=数据库连接]').textContent.includes('SQLite 文件名')"), false);
  await category("备份与恢复");
  assert(await read(page, "!document.querySelector('[aria-label=日常灾备] input[type=checkbox]').checked"));
  assert.equal(await read(page, "window.__calls.some(c=>['runBackupPlan','getCloudBackupStatus'].includes(c.name))"), false);
  await category("单据设置"); await field(page, "明细空白行数", "9");
  await clickText(page, "恢复本分类默认"); await clickText(page, "恢复默认值", "[role=dialog] button");
  const resetSaved = await save();
  assert.equal(resetSaved.settings.system.databaseProvider, "PostgreSQL");
  assert.equal(resetSaved.settings.system.postgreSqlHost, "database.example.test");
  assert.equal(resetSaved.settings.system.postgreSqlAutoBackupSchedule, "Daily");
  assert.equal(resetSaved.settings.system.postgreSqlAutoBackupTime, "02:00");
  assert.equal(resetSaved.settings.system.itemEntryBlankRowCount, 20);
  await category("备份与恢复"); await clickText(page, "恢复与校验");
  await waitFor(page, "window.__calls.some(c=>c.name==='listPostgreSqlPhysicalBackups')");
  await waitFor(page, "document.querySelector('.maintenance-database-recovery-panel input') && !document.querySelector('.maintenance-database-recovery-panel input').disabled");
  await clickText(page, "恢复数据库（回到某份备份）", "summary");
  await clickText(page, "从外部完整包恢复", "summary");
  await clickText(page, "服务器恢复与升级步骤", "summary");
  await audit(page, "settings-postgresql-backup-and-scoped-defaults");
  for (const width of [390, 320]) {
    await start("&section=postgresql&provider=PostgreSQL", width);
    await waitFor(page, "document.querySelector('.maintenance-full-migration-panel')");
    for (const title of ["恢复数据库（回到某份备份）", "从外部完整包恢复", "服务器恢复与升级步骤"]) await clickText(page, title, "summary");
    await audit(page, "settings-postgresql-maintenance-" + width);
  }

  await start("&section=backupPolicy&provider=PostgreSQL", 390);
  await waitFor(page, "document.querySelector('[aria-label=日常灾备] input')");
  await click(page, '[aria-label="日常灾备"] input[type=checkbox]');
  await textField("备份包密码", "backup-password-2026");
  await textField("再次输入新密码", "backup-password-2026");
  await clickText(page, "异机副本", ".settings-section-nav button");
  await textField("副本存储", "S3");
  await textField("S3 端点", "https://objects.example.test");
  await textField("存储桶", "backup-bucket");
  await textField("Access Key ID", "test-access");
  await clickText(page, "修改Secret Access Key（未设置）", "label");
  await textField("Secret Access Key", "fixture-secret");
  await audit(page, "settings-s3-replica-390");
  await clickText(page, "保存灾备设置");
  await waitFor(page, "window.__calls.some(c=>c.name==='saveBackupPlan') && !document.querySelector('.backup-plan-toolbar').textContent.includes('有未保存')");
  const planSaved = await read(page, "window.__calls.filter(c=>c.name==='saveBackupPlan').at(-1).input.body");
  assert.equal(planSaved.plan.enabled, true); assert.equal(planSaved.plan.replicaMode, "S3");
  assert.equal(planSaved.s3SecretAccessKey, "fixture-secret"); assert.equal(planSaved.plan.s3.bucket, "backup-bucket");
  await clickText(page, "测试已保存的副本存储");
  await waitFor(page, "window.__calls.some(c=>c.name==='testBackupReplica')");
  await textField("对象前缀", "pending-edit");
  await read(page, "window.__changeBackupPlanOnServer()");
  await clickText(page, "保存灾备设置");
  await waitFor(page, "document.body.innerText.includes('数据已被其他用户更新')");
  assert.equal(await read(page, "[...document.querySelectorAll('label')].find(n=>n.querySelector(':scope>span')?.textContent==='对象前缀').querySelector('input').value"), "pending-edit");
  await category("维护工具"); await clickText(page, "取消", "[role=dialog] button");
  assert(await read(page, "window.__route.includes('backupReplica')"));
  await captureScreenshot(page, path.join(output, "settings-s3-conflict-and-draft.png"));
  results.push("settings-backup-opt-in-s3-save-test-conflict-and-draft-protection");

  await start("&edition=Sales&desktop=1&section=documentFields");
  assert.equal(await read(page, "document.querySelector('.settings-category-nav').textContent.includes('单据设置')"), false);
  assert(await read(page, "Boolean(document.querySelector('[aria-label=数据库连接]'))"));
  await audit(page, "settings-Sales-category-filter");
  await start("&section=documentFields&group=payment&role=reader", 390);
  assert.equal(await read(page, "document.querySelector('[aria-label=单据字段名称] select').value"), "payment");
  assert(await read(page, "[...document.querySelectorAll('[aria-label=单据字段名称] input')].every(n=>n.matches(':disabled'))"));
  await audit(page, "settings-reader-deep-link");

  for (const width of [1440, 900, 390, 320]) {
    await start("&section=documentFields&group=item", width);
    await audit(page, `settings-fields-${width}`);
    await captureScreenshot(page, path.join(output, `settings-fields-${width}.png`));
  }
  await start(); await read(page, "window.__failHealth=true"); await category("运行与数据库");
  await waitFor(page, "document.body.innerText.includes('无法读取当前数据库类型')");
  assert.equal(await read(page, "document.querySelector('[aria-label=数据库连接]')"), null);
  await read(page, "window.__failHealth=false"); await clickText(page, "重试运行状态");
  await waitFor(page, "document.querySelector('[aria-label=数据库连接]')");
  results.push("settings-runtime-status-failure-and-retry");

  await open("payment"); page = currentPage();
  await input(page, '#payment-basic-section input', "未保存单据");
  await click(page, "#payment-tab-business");
  await click(page, '.document-spare-fields summary'); await clickText(page, "设置字段名称");
  await clickText(page, "取消", "[role=dialog] button");
  assert(await read(page, "window.__route.startsWith('/payments/')"));
  await clickText(page, "设置字段名称"); await clickText(page, "打开单据设置", "[role=dialog] button");
  await waitFor(page, "document.querySelector('[aria-label=单据字段名称] select')?.value==='payment'");
  results.push("settings-business-shortcut-guards-unsaved-payment");
}
