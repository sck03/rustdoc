import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { withOfficeUi } from '../../../../scripts/lib/native-office-ui.mjs';

await withOfficeUi('frontend-filter-typography-ui', async ({ repo, output, url, openPage }) => {
  const page = await openPage('oa-review', 'Review-2026-Test');
  const routes = await page.locator('.workspace-nav a[href]').evaluateAll(nodes => [...new Set(nodes.map(node => node.hash).filter(Boolean))]);
  assert(routes.length >= 25, 'Audit the complete authorized navigation, rather than a single representative page');
  routes.push(...['calendar', 'handling', 'delegations', 'templates', 'runtime'].map(view => `#/office/approval-settings?view=${view}`));
  routes.push(...['examples', 'history', 'transfer', 'annual', 'online'].map(section => `#/master-data/hs-knowledge/${section}`));
  const results = [];
  for (const width of [1440, 1024, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    for (const route of routes) {
      await page.goto(url + '/' + route);
      await page.locator('.workspace-header h1').waitFor();
      await page.getByText('正在加载页面', { exact: true }).waitFor({ state: 'hidden' });
      await page.waitForFunction(() => ![...document.querySelectorAll('.page-state[data-tone="loading"]')].some(node => node.getClientRects().length));
      await page.locator('[aria-busy="true"], .oa-hub-count[aria-label="正在读取"]').first().waitFor({ state: 'hidden' });
      await page.evaluate(() => document.fonts.ready);
      const metrics = await page.evaluate(() => {
        const fields = [...document.querySelectorAll('[data-ui="filter-bar"] [data-ui="field"], [data-ui="field"][data-inline="true"], .office-toolbar [data-ui="field"]')];
        const invalid = fields.flatMap(field => {
          if (!field.getClientRects().length) return [];
          const label = field.querySelector(':scope > .form-field-label');
          const control = field.querySelector(':scope > input, :scope > select, :scope > .remote-select, :scope > .remote-select-controls');
          if (!label || !control || !control.getClientRects().length) return [];
          const a = label.getBoundingClientRect(), b = control.getBoundingClientRect();
          return b.left >= a.right + 3 && Math.abs((a.top + a.bottom - b.top - b.bottom) / 2) <= 2
            ? [] : [{ label: label.textContent, labelRect: a.toJSON(), controlRect: b.toJSON() }];
        });
        const search = document.querySelector('.nav-search input');
        return { invalid, fields: fields.filter(field => field.getClientRects().length).length,
          primaryActions: [...document.querySelectorAll('main .command-button:not(.secondary)')].filter(node => node.getClientRects().length).map(node => node.textContent),
          font: getComputedStyle(document.body).fontFamily,
          searchBackground: search ? getComputedStyle(search).backgroundColor : null,
          overflow: document.documentElement.scrollWidth > innerWidth + 1,
          overflowElements: [...document.querySelectorAll('main *')].filter(node => node.getClientRects().length && node.getBoundingClientRect().right > innerWidth + 1).slice(0,8).map(node => ({ tag: node.tagName, className: node.className, right: node.getBoundingClientRect().right })) };
      });
      assert.deepEqual(metrics.invalid, [], `${width} ${route}: labels and filter controls share a row`);
      assert.equal(metrics.overflow, false, `${width} ${route}: no page overflow ${JSON.stringify(metrics.overflowElements)}`);
      assert(metrics.primaryActions.length <= 1, `${width} ${route}: one primary action ${JSON.stringify(metrics.primaryActions)}`);
      assert(metrics.font.replaceAll('"', '').startsWith('Noto Sans CJK SC'), `${route}: use the bundled interface family (${metrics.font})`);
      if (metrics.searchBackground) assert.equal(metrics.searchBackground, 'rgba(0, 0, 0, 0)', 'Navigation search stays transparent');
      results.push({ width, route, ...metrics });
      console.log(`[layout] ${width} ${route}: ${metrics.fields} filter fields`);
      if (width === 1440 || route.includes('/query') || route === '#/dashboard' || route.includes('/jobs')) {
        await page.screenshot({ path: path.join(output, `${width}-${route.replace(/[^a-z0-9]/gi, '_')}.png`), fullPage: true });
      }
    }
  }
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(url + '/#/master-data/hs-knowledge/examples');
  await page.getByRole('button', { name: '新增实例', exact: true }).click();
  assert.deepEqual(await page.locator('main .command-button:not(.secondary)').allTextContents(), ['保存实例'], 'Editing makes save the primary action');
  await page.getByRole('button', { name: '取消', exact: true }).click();
  await page.goto(url + '/#/master-data/hs-knowledge/transfer');
  const exportRoute = '**/api/master-data/hs-knowledge/export';
  await page.route(exportRoute, route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: '知识库导出服务暂不可用' }) }));
  await page.getByRole('button', { name: '导出 .edmhs', exact: true }).click();
  await page.getByText('知识库导出服务暂不可用', { exact: true }).waitFor();
  await page.unroute(exportRoute);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: '导出 .edmhs', exact: true }).click();
  assert((await download).suggestedFilename().endsWith('.edmhs'), 'Export can be retried after a failure');
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(url);
  await page.locator('.workspace-header h1').waitFor();
  await page.evaluate(() => document.fonts.ready);
  const session = await page.context().newCDPSession(page);
  await session.send('DOM.enable'); await session.send('CSS.enable');
  const { root } = await session.send('DOM.getDocument');
  const { nodeId } = await session.send('DOM.querySelector', { nodeId: root.nodeId, selector: '.workspace-header h1' });
  const { fonts } = await session.send('CSS.getPlatformFontsForNode', { nodeId });
  assert(fonts.some(font => font.isCustomFont && font.familyName.includes('Noto Sans CJK SC') && font.glyphCount > 0), 'The title renders real bundled glyphs');
  await page.evaluate(() => document.fonts.load('400 14px "Noto Serif CJK SC"', '中文报表'));
  const manifest = JSON.parse(fs.readFileSync(path.join(repo, 'Resources/Fonts/OpenSource/font-manifest.json'), 'utf8'));
  const assets = fs.readdirSync(path.join(repo, 'apps/export-doc-web/dist/assets')).filter(name => name.endsWith('.woff2'));
  assert.equal(assets.length, manifest.fonts.length, 'The UI contains every approved web font face');
  const expectedHashes = new Set(manifest.fonts.map(font => font.web.sha256.toLowerCase()));
  for (const asset of assets) {
    const sha = createHash('sha256').update(fs.readFileSync(path.join(repo, 'apps/export-doc-web/dist/assets', asset))).digest('hex');
    assert(expectedHashes.delete(sha), `Unique approved web font: ${asset}`);
  }
  assert.equal(expectedHashes.size, 0, 'All approved web font hashes are present');
  await page.goto(url + '/#/crm/email-templates');
  await page.getByRole('button', { name: '新建模板', exact: true }).click();
  await page.getByLabel('模板名称', { exact: true }).fill('字体回归');
  await page.getByLabel('邮件主题', { exact: true }).fill('中文预览');
  await page.getByText('高级 HTML', { exact: true }).click();
  await page.getByLabel('邮件正文高级 HTML').fill('<p>中文字体预览 ABC123</p><script>parent.__previewScriptExecuted=true</script>');
  await page.getByRole('tab', { name: '变量设置', exact: true }).click();
  await page.getByRole('button', { name: '生成预览', exact: true }).click();
  const frame = page.frameLocator('iframe[title="邮件模板正文预览"]');
  await frame.getByText('中文字体预览 ABC123', { exact: true }).waitFor();
  await frame.locator('body').evaluate(() => document.fonts.ready);
  const preview = await page.locator('iframe[title="邮件模板正文预览"]').evaluate(node => {
    const doc = node.contentDocument;
    const script = doc.createElement('script'); script.textContent = 'parent.__previewScriptExecuted=true'; doc.body.appendChild(script);
    return { sandbox: node.getAttribute('sandbox'), font: getComputedStyle(doc.body).fontFamily, faces: [...doc.fonts].map(face => ({ family: face.family, status: face.status })), parentFaces: [...document.fonts].map(face => ({ family: face.family, status: face.status })) };
  });
  assert.equal(preview.sandbox, 'allow-same-origin', 'Preview permits same-origin access while retaining the other sandbox restrictions');
  assert(preview.font.includes('Noto Sans CJK SC') && preview.faces.some(face => face.family.includes('Noto Sans CJK SC') && face.status === 'loaded'), `Preview reuses loaded bundled faces: ${JSON.stringify(preview)}`);
  assert.equal(await page.evaluate(() => window.__previewScriptExecuted), undefined, 'The preview sandbox still blocks script execution');
  await page.screenshot({ path: path.join(output, 'email-preview-fonts.png'), fullPage: true });
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ routes, results, fonts, assets, preview }, null, 2));
  console.log(`Filter and typography audit passed: ${results.length} real page views, ${routes.length} routes`);
});
