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
    const selector = await read(page, `(()=>{const node=[...document.querySelectorAll('label')].find(n=>n.querySelector(':scope>span')?.textContent===${JSON.stringify(label)})?.querySelector('input');if(!node)throw new Error('Missing '+${JSON.stringify(label)});node.dataset.settingsField='current';return '[data-settings-field=current]'})()`);
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
  await category("备份与恢复"); await clickText(page, "WebDAV 云备份", ".settings-section-nav button");
  await textField("WebDAV 地址", "https://backup.example.test/dav");
  await category("维护工具"); await waitFor(page, "document.querySelector('[aria-label=日志管理]')");
  await field(page, "日志保留天数", "60");
  assert(await read(page, "[...document.querySelectorAll('button')].find(n=>n.textContent.trim()==='清理旧日志').disabled"));
  assert.equal(await read(page, "window.__calls.filter(c=>c.name==='cleanupSystemLogs').length"), 0);
  assert(await read(page, "['单据设置','邮件设置','备份与恢复','维护工具'].every(label=>document.querySelector('.settings-command-heading').textContent.includes(label))"));
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

  await start("&section=database&desktop");
  assert(await read(page, "document.querySelector('[aria-label=数据库连接]').textContent.includes('SQLite 文件名')"));
  assert.equal(await read(page, "document.querySelector('[aria-label=数据库连接]').textContent.includes('服务器')"), false);
  assert(await read(page, "Boolean(document.querySelector('[aria-label=软件更新]'))"));
  await category("备份与恢复");
  assert.equal(await read(page, "document.querySelector('.settings-section-nav').textContent.includes('团队库')"), false);
  await clickText(page, "数据备份与还原", ".settings-section-nav button");
  await waitFor(page, "window.__calls.some(c=>c.name==='listDatabaseBackups')");
  await audit(page, "settings-sqlite-backup-tools");

  await start("&section=database&provider=PostgreSQL");
  assert.equal(await read(page, "document.querySelector('[aria-label=软件更新]')"), null);
  assert.equal(await read(page, "document.querySelector('[aria-label=数据库连接]').textContent.includes('SQLite 文件名')"), false);
  await category("备份与恢复");
  await field(page, "自动备份周期", "Weekly"); await field(page, "每周备份星期", "3");
  await category("单据设置"); await field(page, "明细空白行数", "9");
  await clickText(page, "恢复本分类默认"); await clickText(page, "恢复默认值", "[role=dialog] button");
  const resetSaved = await save();
  assert.equal(resetSaved.settings.system.databaseProvider, "PostgreSQL");
  assert.equal(resetSaved.settings.system.postgreSqlHost, "database.example.test");
  assert.equal(resetSaved.settings.system.postgreSqlAutoBackupDayOfWeek, 3);
  assert.equal(resetSaved.settings.system.itemEntryBlankRowCount, 20);
  await category("备份与恢复"); await clickText(page, "团队库与完整迁移");
  await waitFor(page, "window.__calls.some(c=>c.name==='listPostgreSqlPhysicalBackups')");
  await audit(page, "settings-postgresql-backup-and-scoped-defaults");

  for (const edition of ["Sales", "Administration"]) {
    await start(`&edition=${edition}&section=documentFields`);
    assert.equal(await read(page, "document.querySelector('.settings-category-nav').textContent.includes('单据设置')"), false);
    assert(await read(page, "Boolean(document.querySelector('[aria-label=数据库连接]'))"));
    await audit(page, `settings-${edition}-category-filter`);
  }
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
