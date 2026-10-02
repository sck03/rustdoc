import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Keep the public smoke switches, but exercise the current shared React flow.
// Field/permission, race, pagination and PDF-download regressions live in
// test_payment_printing_ui.mjs instead of a second legacy selector framework.
export function createPaymentSmokeScene({ authorizedJsonHeaders, evaluate, waitForPageExpression }) {
  const spec = JSON.parse(readFileSync(new URL('../../crates/export-doc-contracts/src/openapi.json', import.meta.url), 'utf8'));
  const operations = new Map(Object.entries(spec.paths).flatMap(([route, methods]) => Object.entries(methods).map(([method, operation]) => [operation.operationId, { route, method }])));
  async function run(page, options, accessToken, tokenType, timeoutMs) {
    const result = { paymentReportCheck: null, paymentDeleteCheck: null };
    if (!options.paymentReportCheck && !options.paymentDeleteCheck) return result;
    const request = (id, { body, parameters = {}, query = {} } = {}) => {
      const operation = operations.get(id);
      assert(operation, `Unknown payment smoke operation: ${id}`);
      const url = new URL(operation.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key])), options.apiBaseUrl);
      for (const [key, value] of Object.entries(query)) url.searchParams.set(key, value);
      return fetch(url, { method: operation.method, headers: authorizedJsonHeaders(options, accessToken, tokenType),
        ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(timeoutMs) });
    };
    async function json(id, input) {
      const response = await request(id, input);
      assert(response.ok, `${id}: HTTP ${response.status}`);
      return response.json();
    }
    const value = async expression => (await evaluate(page, expression, true)).value;
    const wait = expression => waitForPageExpression(page, expression, timeoutMs, 'Payment workflow did not reach the expected state.');
    async function click(label, scope = 'document') {
      const selector = `[...${scope}.querySelectorAll('button')].find(button => button.textContent.trim() === ${JSON.stringify(label)} && !button.disabled)`;
      await wait(`Boolean(${selector})`);
      await value(`(${selector}).click()`);
    }
    const user = await json('getCurrentUser');
    const businessDate = user.businessDate ?? user.user?.businessDate;
    assert.match(businessDate, /^\d{4}-\d{2}-\d{2}$/u, 'Use the server business date');
    const reference = `SMOKE-PAY-${Date.now()}`;
    const { payment } = await json('CreatePayment', { body: { voucherNo: reference, invoiceNo: reference,
      paymentDate: businessDate, payeeName: '付款打印验收', payerName: '报销验收人',
      department: '测试部门', cnyAmount: 123.45, otherExpense: 123.45, notes: '临时验收记录，完成后清理。' } });
    assert(payment?.id, 'Created payment must have an identity');
    const url = new URL(options.webUrl);
    url.hash = `/payments/${payment.id}?section=report`;
    let failure;
    try {
      await page.send('Page.navigate', { url: url.toString() });
      const panel = `document.querySelector('[aria-label="付款/报销单预览"]')`;
      await wait(`Boolean(${panel})`);
      if (options.paymentReportCheck) {
        const select = `${panel}.querySelector('select')`;
        await wait(`Boolean(${select} && !${select}.disabled)`);
        const templates = await value(`[...${select}.options].map(option => option.value)`);
        assert(templates.length > 0, 'An authorized payment template is available');
        for (const template of templates.slice(0, 2)) {
          await value(`(() => { const select = ${select}; select.value = ${JSON.stringify(template)}; select.dispatchEvent(new Event('change', { bubbles: true })); })()`);
          await click('预览', panel);
          await wait(`Boolean(${panel}.querySelector('iframe')?.srcdoc.includes('<svg'))`);
          await wait(`Boolean([...${panel}.querySelectorAll('button')].find(button => button.textContent.trim() === '打印' && !button.disabled))`);
        }
        await click('基本信息');
        await value(`(() => { const label = [...document.querySelectorAll('#payment-basic-section label')].find(label => label.textContent.trim().startsWith('付款单号')); const input = label.querySelector('input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, ${JSON.stringify(reference + '-EDIT')}); input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
        await click('打印与 PDF');
        await wait(`!${panel}.querySelector('iframe') && [...${panel}.querySelectorAll('button')].some(button => button.textContent.trim() === '导出 PDF' && button.disabled)`);
        await click('保存付款报销');
        await wait(`Boolean([...${panel}.querySelectorAll('button')].find(button => button.textContent.trim() === '导出 PDF' && !button.disabled))`);
        const saved = await json('GetPayment', { parameters: { id: payment.id } });
        assert.equal(saved.voucherNo, reference + '-EDIT');
        result.paymentReportCheck = { passed: true, templates: Math.min(2, templates.length), draftOutputGuard: true };
      }
      if (options.paymentDeleteCheck) {
        await click('删除', `document.querySelector('.editor-toolbar')`);
        await click('确认删除', `document.querySelector('[role="dialog"]')`);
        await wait(`Boolean(document.querySelector('.payment-table'))`);
        assert.equal((await request('GetPayment', { parameters: { id: payment.id } })).status, 404);
        result.paymentDeleteCheck = { passed: true };
      }
    } catch (error) { failure = error; }
    try {
      const current = await request('GetPayment', { parameters: { id: payment.id } });
      if (current.status !== 404) {
        assert(current.ok, `Read payment for cleanup: HTTP ${current.status}`);
        const { rowVersion } = await current.json();
        const removed = await request('DeletePayment', { parameters: { id: payment.id }, query: { rowVersion } });
        assert(removed.ok, `Payment cleanup: HTTP ${removed.status}`);
      }
    } catch (error) { failure = failure ? new AggregateError([failure, error], 'Payment smoke and cleanup failed') : error; }
    if (failure) throw failure;
    return result;
  }
  return { run };
}
