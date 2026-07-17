import { existsSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";

export function createInvoiceLetterOfCreditSmokeScene(runtime) {
  const {
    createSmokeInvoice,
    deleteSmokeInvoice,
    evaluate,
    includesText,
    redactDesktopAccessToken,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForInvoiceLetterOfCreditCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.invoiceLetterOfCreditCheck) {
      return null;
    }
  
    const invoice = await createSmokeInvoice(options, accessToken, tokenType);
    const timestamp = Date.now();
    const marker = `LC-SMOKE-MARKER-${timestamp}`;
    const letterOfCreditPath = path.join(options.userDataDir, `letter-of-credit-smoke-${timestamp}.txt`);
    writeFileSync(
      letterOfCreditPath,
      [
        `DOCUMENTARY CREDIT NO. ${marker}`,
        "APPLICANT: SMOKE BUYER LTD.",
        `BENEFICIARY: ${invoice.exporterNameEN || "Smoke Exporter Ltd."}`,
        `INVOICE: ${invoice.invoiceNo}`,
      ].join("\n"),
      "utf8",
    );
  
    let result = null;
    let deletedInvoice = false;
    let deletedSourceFile = false;
  
    try {
      const checkUrl = buildInvoiceLetterOfCreditCheckUrl(options.webUrl, invoice.id);
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "信用证",
        "导入信用证",
        "来源文件",
        "信用证文本",
        invoice.invoiceNo,
      ];
  
      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
      const sourceInputCheck = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="信用证"]');
          return Boolean(section && Array.from(section.querySelectorAll('.path-field')).some((field) => {
            const label = field.querySelector('.path-field-label');
            return label && (label.innerText || '').trim() === '来源文件' && field.querySelector('input');
          }));
        })()`,
        timeoutMs,
        "Timed out waiting for the letter-of-credit source path input.",
      );
  
      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="信用证"]');
          const field = section && Array.from(section.querySelectorAll('.path-field')).find((candidate) => {
            const label = candidate.querySelector('.path-field-label');
            return label && (label.innerText || '').trim() === '来源文件';
          });
          const input = field && field.querySelector('input');
          if (!input) {
            throw new Error('Letter-of-credit source input was not found.');
          }
  
          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
          setter.call(input, ${JSON.stringify(letterOfCreditPath)});
          input.dispatchEvent(new Event('input', { bubbles: true }));
          return true;
        })()`,
        true,
      );
  
      const importButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="信用证"]');
          const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('导入信用证'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the letter-of-credit import button to become available.",
      );
  
      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="信用证"]');
          const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('导入信用证'));
          if (!button || button.disabled) {
            throw new Error('Letter-of-credit import button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const importedTextCheck = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="信用证"]');
          const textarea = section && section.querySelector('textarea');
          const text = textarea ? textarea.value || '' : '';
          const bodyText = document.body ? document.body.innerText || '' : '';
          return text.includes(${JSON.stringify(marker)}) &&
            text.includes(${JSON.stringify(invoice.invoiceNo)}) &&
            bodyText.includes('信用证已导入');
        })()`,
        timeoutMs,
        `Timed out waiting for imported letter-of-credit text: ${marker}`,
      );
  
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        sourcePath: letterOfCreditPath,
        marker,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        sourceInputCheck,
        importButtonCheck,
        importedTextCheck,
        deletedInvoice,
        deletedSourceFile,
      };
    } finally {
      deletedInvoice = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      try {
        rmSync(letterOfCreditPath, { force: true });
        deletedSourceFile = !existsSync(letterOfCreditPath);
      } catch {
        deletedSourceFile = false;
      }
  
      if (result) {
        result.deletedInvoice = deletedInvoice;
        result.deletedSourceFile = deletedSourceFile;
      }
    }
  
    return result;
  }

  function buildInvoiceLetterOfCreditCheckUrl(webUrl, invoiceId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeLetterOfCredit", String(invoiceId));
    url.hash = `/invoices/${invoiceId}`;
    return url.toString();
  }

  return { run: waitForInvoiceLetterOfCreditCheck };
}
