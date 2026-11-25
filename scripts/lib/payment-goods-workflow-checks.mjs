import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyPaymentGoods({ page, url, invoke, token, payment, output }) {
  const seeded = (await invoke('UpdatePayment', { ...payment, payerName: '样例贸易有限公司', payeeName: '样例服饰有限公司',
    department: '业务部', invoiceNo: 'FACTORY-2026-001', bankName: '样例银行支行', accountNo: 'TEST-ACCOUNT-001',
    paymentMethod: '电汇', tradeMethod: 'T/T', taxRebateRate: '13%', shipmentCountry: '美国', cnyAmount: '232151.04',
    goodsItems: [{ goodsName: '付款货物', quantity: '12.5', quantityUnit: '件' }] },
    { id: payment.id }, token)).payment;
  assert.deepEqual(seeded.goodsItems, [{ goodsName: '付款货物', quantity: '12.5', quantityUnit: '件' }]);
  const goods = [
    { goodsName: '男式针织上衣', quantity: '8448', quantityUnit: '件' },
    { goodsName: '女式棉衬衫', quantity: '3200', quantityUnit: '件' },
    { goodsName: '棉布', quantity: '120.125', quantityUnit: '米' },
    { goodsName: '包装纸箱', quantity: '42', quantityUnit: '箱' },
  ];
  await page.goto(`${url}/#/payments/${payment.id}?section=business`);
  for (let i = 0; i < goods.length; i++) {
    if (i) await page.getByRole('button', { name: '新增货物', exact: true }).click();
    for (const [field, label] of [['goodsName', '货物品名'], ['quantity', '数量'], ['quantityUnit', '数量单位']]) {
      await page.getByRole('textbox', { name: `第 ${i + 1} 项${label}`, exact: true }).fill(goods[i][field]);
    }
  }
  assert(await page.getByRole('button', { name: '新增货物', exact: true }).isDisabled());
  const composed = await page.getByRole('textbox', { name: '第 1 项货物品名', exact: true }).evaluate(input => {
    const event = new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true });
    input.dispatchEvent(event);
    return { prevented: event.defaultPrevented, value: input.value };
  });
  assert.deepEqual(composed, { prevented: false, value: goods[0].goodsName });
  const save = async () => {
    const response = page.waitForResponse(response => response.url().endsWith(`/api/payments/${payment.id}`) && response.request().method() === 'PUT');
    await page.getByRole('button', { name: '保存付款报销', exact: true }).click();
    const saved = await response;
    assert.equal(saved.status(), 200, await saved.text());
    await page.getByText('付款报销已保存。', { exact: true }).waitFor();
    return (await saved.json()).payment;
  };
  const saved = await save();
  assert.deepEqual(saved.goodsItems, goods);
  assert.equal(saved.cnyAmount, seeded.cnyAmount);
  await page.reload();
  assert.equal(await page.getByRole('textbox', { name: '第 3 项数量', exact: true }).inputValue(), '120.125');
  await page.screenshot({ path: path.join(output, 'payment-four-goods.png'), fullPage: true });
  for (const width of [1180, 680]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.screenshot({ path: path.join(output, `payment-four-goods-${width}.png`), fullPage: true });
    assert(await page.getByRole('textbox', { name: '第 4 项货物品名', exact: true }).isVisible());
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1), false);
  }
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole('tab', { name: '打印与 PDF', exact: true }).click();
  const preview = page.locator('[aria-label="付款/报销单预览"]');
  await preview.getByRole('combobox', { name: '模板', exact: true }).locator('option[value$="factory_payment_template.dtpl"]').waitFor({ state: 'attached' });
  const template = await preview.getByRole('combobox', { name: '模板', exact: true }).locator('option').evaluateAll(options =>
    options.find(option => option.value.endsWith('factory_payment_template.dtpl'))?.value);
  assert(template, 'The authorized default catalog includes the factory payment template');
  await preview.getByRole('combobox', { name: '模板', exact: true }).selectOption(template);
  await preview.getByRole('button', { name: '预览', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('iframe[title="付款/报销单预览"]')?.srcdoc.includes('<svg'));
  const download = page.waitForEvent('download');
  await preview.getByRole('button', { name: '导出 PDF', exact: true }).click();
  await (await download).saveAs(path.join(output, 'factory-payment.pdf'));
  await page.screenshot({ path: path.join(output, 'factory-payment-preview.png'), fullPage: true });
  await assert.rejects(invoke('CreatePayment', { ...saved, id: 0, rowVersion: '', goodsItems: [...goods, goods[0]] }, {}, token), /\(400\)/);
  await assert.rejects(invoke('CreatePayment', { ...saved, id: 0, rowVersion: '', goodsItems: [{ ...goods[0], quantityUnit: '箱'.repeat(21) }] }, {}, token), /\(400\)/);
  await page.getByRole('tab', { name: '业务信息', exact: true }).click();
  await page.getByRole('button', { name: '删除第 2 项货物', exact: true }).click();
  assert.deepEqual((await save()).goodsItems, [goods[0], goods[2], goods[3]]);
  for (let i = 0; i < 3; i++) await page.getByRole('button', { name: '删除第 1 项货物', exact: true }).click();
  assert.deepEqual((await save()).goodsItems, []);
  await page.reload();
  assert.equal(await page.getByRole('textbox', { name: '第 1 项货物品名', exact: true }).inputValue(), '');
}
