export function createInvoiceReportSmokeScene(runtime) {
  const {
    buildInvoiceReportCheckUrl,
    createSmokeInvoice,
    createSmokeProduct,
    deleteSmokeInvoice,
    deleteSmokeProduct,
    evaluate,
    includesText,
    invoiceDocumentOutputSmokeScene,
    invoiceItemTableSmokeScene,
    invoiceShippingMarkSmokeScene,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForInvoiceReportCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.invoiceReportCheck) {
      return null;
    }
  
    const invoice = await createSmokeInvoice(options, accessToken, tokenType);
    const product = await createSmokeProduct(options, accessToken, tokenType);
    let result = null;
    let deleted = false;
    let deletedProduct = false;
  
    try {
      const checkUrl = buildInvoiceReportCheckUrl(options.webUrl, invoice.id);
      await page.send("Page.navigate", { url: checkUrl });
      const editorExpectedText = [
        "基础信息",
        "利润分析",
        "预估毛利",
        "毛利率",
        invoice.invoiceNo,
      ];
      const editorPageText = await waitForRuntimeDiagnostics(page, editorExpectedText, timeoutMs);
  
      await evaluate(
        page,
        `(() => {
          const navigation = document.querySelector('[aria-label="发票编辑分区"]');
          const previewButton = navigation
            ? Array.from(navigation.querySelectorAll('button')).find((button) => (button.innerText || '').includes('预览导出'))
            : null;
          if (!previewButton) {
            throw new Error('Invoice preview/export navigation button was not found.');
          }
  
          previewButton.click();
          const reportSection = document.getElementById('invoice-report-section');
          reportSection?.scrollIntoView({ block: 'start', behavior: 'auto' });
          const advancedDetails = reportSection?.querySelector('details.report-export-advanced');
          if (advancedDetails && !advancedDetails.open) {
            advancedDetails.querySelector('summary')?.click();
          }
          return true;
        })()`,
        true,
      );
  
      const reportExpectedText = [
        "报表预览",
        "模板",
        "模板设置",
        "输出 PDF",
        "生成 PDF",
        "打印",
        "单据包",
        "预览单据包",
        "输出 ZIP",
        "合并 PDF",
        "生成 ZIP",
        "邮件附件",
        "收件人",
        "邮件设置",
        "发送邮件",
      ];
      const reportPageText = await waitForRuntimeDiagnostics(page, reportExpectedText, timeoutMs);
      const expectedText = [...editorExpectedText, ...reportExpectedText];
      const pageText = `${editorPageText}\n${reportPageText}`;
      const previewButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').trim() === '预览');
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice report preview button to become available.",
      );
  
      const batchExportSettingsButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('模板设置'));
          return Boolean(button && !button.disabled && (button.title || '').includes('管理单证模板'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice batch export settings button.",
      );
  
      const documentPackagePreviewButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('预览单据包'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice document package preview button to become available.",
      );
  
      const documentPackageCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const output = panel ? panel.querySelector('.document-package-output') : null;
          const defaultFileName = output ? output.getAttribute('data-default-file-name') || '' : '';
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const text = panel ? panel.innerText || '' : '';
          return Boolean(panel &&
            panel.querySelector('.document-package-template-list') &&
            output &&
            output.querySelector('input') &&
            panel.querySelector('.document-package-output .document-package-zip-check input[type="checkbox"]') &&
            panel.querySelector('.document-package-output .document-package-merge-check input[type="checkbox"]') &&
            defaultFileName.includes(${JSON.stringify(invoice.invoiceNo)}) &&
            defaultFileName.endsWith('.zip') &&
            text.includes('单据包') &&
            text.includes('输出 ZIP') &&
            text.includes('合并 PDF') &&
            buttons.some((element) => (element.innerText || '').includes('生成 ZIP')));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice document package controls.",
      );
  
      const documentEmailCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const emailPanel = panel ? panel.querySelector('.document-email-panel') : null;
          const buttons = emailPanel ? Array.from(emailPanel.querySelectorAll('button')) : [];
          const text = emailPanel ? emailPanel.innerText || '' : '';
          return Boolean(emailPanel &&
            emailPanel.querySelector('input[type="email"]') &&
            emailPanel.querySelector('textarea') &&
            text.includes('邮件附件') &&
            text.includes('收件人') &&
            buttons.some((element) => (element.innerText || '').includes('发送邮件')));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice document email controls.",
      );
  
      const documentEmailSettingsButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const emailPanel = panel ? panel.querySelector('.document-email-panel') : null;
          const buttons = emailPanel ? Array.from(emailPanel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('邮件设置'));
          return Boolean(button && !button.disabled && (button.title || '').includes('管理邮件设置'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice document email settings button.",
      );
      const documentPackageJobCheck = await invoiceDocumentOutputSmokeScene.runPackageJobs(
        page,
        options,
        accessToken,
        tokenType,
        invoice,
        timeoutMs,
      );
      const documentEmailJobCheck = await invoiceDocumentOutputSmokeScene.runDocumentEmailJob(
        page,
        options,
        accessToken,
        tokenType,
        invoice,
        timeoutMs,
      );
  
      const invoiceItemRowActionsCheck = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
          const table = section ? section.querySelector('.item-editor-table') : null;
          const firstRow = table ? table.querySelector('tbody tr') : null;
          const buttons = firstRow ? Array.from(firstRow.querySelectorAll('button')) : [];
          const titles = buttons.map((button) => button.title || button.getAttribute('aria-label') || '');
          return Boolean(table &&
            firstRow &&
            titles.includes('复制新增明细行') &&
            titles.includes('上移明细行') &&
            titles.includes('下移明细行') &&
            titles.includes('删除明细'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice item row actions.",
      );
  
      const invoiceItemClipboardActionsCheck = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
          const toolbar = section ? section.querySelector('[aria-label="明细编辑工具"]') : null;
          const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
          const titles = buttons.map((button) => button.title || button.getAttribute('aria-label') || '');
          return Boolean(toolbar &&
            titles.includes('从剪贴板粘贴明细') &&
            titles.includes('向下填充当前单元格') &&
            titles.includes('撤销明细编辑') &&
            titles.includes('重做明细编辑'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice item clipboard actions.",
      );
  
      const invoiceItemShortcutGuideCheck = await waitForPageExpression(
        page,
        `(() => {
          const guide = document.querySelector('[aria-label="商品明细键盘快捷键说明"]');
          const text = guide ? guide.innerText || '' : '';
          return Boolean(guide &&
            text.includes('Enter / Tab') &&
            text.includes('Ctrl + ↑ ↓') &&
            text.includes('Ctrl + D') &&
            text.includes('Ctrl + Z / Y') &&
            text.includes('Insert'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice item keyboard shortcut guide.",
      );
  
      const {
        autocompleteCheck: invoiceItemAutocompleteCheck,
        cellSelectionCheck: invoiceItemCellSelectionCheck,
        columnVisibilityCheck: invoiceItemColumnVisibilityCheck,
        keyboardNavigationCheck: invoiceItemKeyboardNavigationCheck,
        productLibraryCheck: invoiceItemProductLibraryCheck,
        undoRedoCheck: invoiceItemUndoRedoCheck,
        workbenchModeCheck: invoiceItemWorkbenchModeCheck,
      } = await invoiceItemTableSmokeScene.run(page, product, timeoutMs);
  
      const profitButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="利润分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('计算'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice profit-analysis button to become available.",
      );
  
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="利润分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('计算'));
          if (!button || button.disabled) {
            throw new Error('Invoice profit-analysis button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const profitResultCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="利润分析"]');
          const text = panel ? panel.innerText || '' : '';
          return text.includes('7.0000') &&
            text.includes('- ¥ 113.00') &&
            text.includes('+ ¥ 13.00') &&
            text.includes('¥ 763.80');
        })()`,
        timeoutMs,
        `Timed out waiting for invoice profit-analysis result: ${invoice.invoiceNo}`,
      );
  
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').trim() === '预览');
          if (!button || button.disabled) {
            throw new Error('Invoice report preview button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      let latestInvoicePreviewState = null;
      const previewFrameCheck = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => {
            const panel = document.querySelector('[aria-label="报表预览"]');
            const frame = document.querySelector('iframe[title="报表 HTML 预览"]');
            const srcdoc = frame ? (frame.getAttribute('srcdoc') || frame.srcdoc || '') : '';
            const buttons = panel ? Array.from(panel.querySelectorAll('button')).map((button) => ({
              text: button.innerText || button.textContent || '',
              title: button.title || '',
              disabled: Boolean(button.disabled)
            })) : [];
            const templateSelect = panel ? panel.querySelector('select') : null;
            const alerts = panel ? Array.from(panel.querySelectorAll('.alert, .success-alert, .info-alert')).map((element) => element.innerText || '') : [];
            return {
              found: srcdoc.includes(${JSON.stringify(invoice.invoiceNo)}) &&
                srcdoc.includes(${JSON.stringify(invoice.customerNameEN)}),
              frameExists: Boolean(frame),
              srcdocLength: srcdoc.length,
              srcdocExcerpt: srcdoc.slice(0, 1200),
              selectedTemplatePath: templateSelect ? templateSelect.value || '' : '',
              alerts,
              buttons,
              panelExcerpt: panel ? (panel.innerText || '').slice(0, 1600) : '',
            };
          })()`,
          true,
        ).catch((error) => ({ value: { found: false, error: String(error) } }));
        latestInvoicePreviewState = state.value ?? null;
        return latestInvoicePreviewState && latestInvoicePreviewState.found
          ? latestInvoicePreviewState
          : null;
      }, timeoutMs, () =>
        [
          `Timed out waiting for invoice report preview HTML: ${invoice.invoiceNo}`,
          latestInvoicePreviewState
            ? `Invoice preview state: ${JSON.stringify(latestInvoicePreviewState, null, 2)}`
            : "Invoice preview state: <empty>",
        ].join("\n"),
      );
  
      const draftCustomerName = `Smoke Draft Customer ${invoice.id}`;
      await evaluate(
        page,
        `(() => {
          const labels = Array.from(document.querySelectorAll('label'));
          const label = labels.find((element) => ((element.querySelector('span') || {}).innerText || '').trim() === '客户英文名');
          const input = label ? label.querySelector('input') : null;
          if (!input || input.disabled) {
            throw new Error('Invoice customer English name input is not editable.');
          }
  
          const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
          if (valueSetter) {
            valueSetter.call(input, ${JSON.stringify(draftCustomerName)});
          } else {
            input.value = ${JSON.stringify(draftCustomerName)};
          }
          input.dispatchEvent(new Event('input', { bubbles: true }));
          return input.value;
        })()`,
        true,
      );
  
      const draftInputCheck = await waitForPageExpression(
        page,
        `(() => {
          const labels = Array.from(document.querySelectorAll('label'));
          const label = labels.find((element) => ((element.querySelector('span') || {}).innerText || '').trim() === '客户英文名');
          const input = label ? label.querySelector('input') : null;
          return Boolean(input && input.value === ${JSON.stringify(draftCustomerName)});
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice draft customer field to update.",
      );
  
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').trim() === '预览');
          if (!button || button.disabled) {
            throw new Error('Invoice draft report preview button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const draftPreviewFrameCheck = await waitForPageExpression(
        page,
        `(() => {
          const frame = document.querySelector('iframe[title="报表 HTML 预览"]');
          const srcdoc = frame ? (frame.getAttribute('srcdoc') || frame.srcdoc || '') : '';
          const panel = document.querySelector('[aria-label="报表预览"]');
          const text = panel ? panel.textContent || '' : '';
          return srcdoc.includes(${JSON.stringify(invoice.invoiceNo)}) &&
            srcdoc.includes(${JSON.stringify(draftCustomerName)}) &&
            !srcdoc.includes(${JSON.stringify(invoice.customerNameEN)}) &&
            text.includes('草稿');
        })()`,
        timeoutMs,
        `Timed out waiting for invoice draft report preview HTML: ${invoice.invoiceNo}`,
      );
  
      const draftSavedOutputGuardCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const text = panel ? panel.textContent || '' : '';
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const packageButton = buttons.find((element) => (element.textContent || '').includes('预览单据包'));
          return Boolean(panel &&
            text.includes('当前发票有未保存修改') &&
            text.includes('请先保存后再生成') &&
            packageButton &&
            packageButton.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for invoice saved-output guard while draft changes are unsaved.",
      );
  
      await evaluate(
        page,
        `(() => {
          const labels = Array.from(document.querySelectorAll('label'));
          const label = labels.find((element) => ((element.querySelector('span') || {}).innerText || '').trim() === '客户英文名');
          const input = label ? label.querySelector('input') : null;
          if (!input || input.disabled) {
            throw new Error('Invoice customer English name input is not editable for restore.');
          }
  
          const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
          if (valueSetter) {
            valueSetter.call(input, ${JSON.stringify(invoice.customerNameEN)});
          } else {
            input.value = ${JSON.stringify(invoice.customerNameEN)};
          }
          input.dispatchEvent(new Event('input', { bubbles: true }));
          return input.value;
        })()`,
        true,
      );
  
      const draftRestoreCheck = await waitForPageExpression(
        page,
        `(() => {
          const labels = Array.from(document.querySelectorAll('label'));
          const label = labels.find((element) => ((element.querySelector('span') || {}).innerText || '').trim() === '客户英文名');
          const input = label ? label.querySelector('input') : null;
          return Boolean(input && input.value === ${JSON.stringify(invoice.customerNameEN)});
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice customer field to restore after draft preview check.",
      );
  
      const savedOutputRestoredCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const text = panel ? panel.textContent || '' : '';
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const packageButton = buttons.find((element) => (element.textContent || '').includes('预览单据包'));
          return Boolean(panel &&
            !text.includes('当前发票有未保存修改') &&
            packageButton &&
            !packageButton.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for saved-output actions to recover after restoring the invoice draft.",
      );
  
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.textContent || '').includes('预览单据包'));
          if (!button || button.disabled) {
            throw new Error('Invoice document package preview button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      let latestDocumentPackagePreviewState = null;
      const documentPackagePreviewFrameCheck = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => {
            const panel = document.querySelector('[aria-label="报表预览"]');
            const previewList = panel ? panel.querySelector('.document-package-preview-list') : null;
            const frames = previewList ? Array.from(previewList.querySelectorAll('iframe[title^="单据包 HTML 预览"]')) : [];
            const srcdocs = frames.map((frame) => frame.getAttribute('srcdoc') || frame.srcdoc || '');
            const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
            const packageButton = buttons.find((element) => (element.innerText || '').includes('预览单据包'));
            const alerts = panel ? Array.from(panel.querySelectorAll('.alert, .success-alert')).map((element) => element.innerText || '') : [];
            return {
              found: frames.length > 0 && srcdocs.some((srcdoc) => srcdoc.includes(${JSON.stringify(invoice.invoiceNo)})),
              frameCount: frames.length,
              previewListExists: Boolean(previewList),
              packageButtonDisabled: packageButton ? packageButton.disabled : null,
              alerts,
              panelExcerpt: panel ? (panel.innerText || '').slice(0, 1600) : '',
              firstFrameExcerpt: srcdocs[0] ? srcdocs[0].slice(0, 800) : '',
            };
          })()`,
          true,
        ).catch((error) => ({ value: { found: false, error: String(error) } }));
        latestDocumentPackagePreviewState = state.value ?? null;
        return latestDocumentPackagePreviewState && latestDocumentPackagePreviewState.found
          ? latestDocumentPackagePreviewState
          : null;
      }, timeoutMs, () =>
        [
          `Timed out waiting for invoice document package preview HTML: ${invoice.invoiceNo}`,
          latestDocumentPackagePreviewState
            ? `Document package preview state: ${JSON.stringify(latestDocumentPackagePreviewState, null, 2)}`
            : "Document package preview state: <empty>",
        ].join("\n"),
      );
  
      const printButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="报表预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('打印'));
          return Boolean(button && !button.disabled && (button.title || '').includes('打印当前预览'));
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice report print button to become available.",
      );
  
      const shippingMarkDesignerCheck = await invoiceShippingMarkSmokeScene.run(
        page,
        options,
        accessToken,
        tokenType,
        invoice,
        timeoutMs,
      );
  
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        previewButtonCheck,
        batchExportSettingsButtonCheck,
        documentPackagePreviewButtonCheck,
        documentPackageCheck,
        documentEmailCheck,
        documentEmailSettingsButtonCheck,
        documentPackageJobCheck,
        documentEmailJobCheck,
        invoiceItemRowActionsCheck,
        invoiceItemClipboardActionsCheck,
        invoiceItemShortcutGuideCheck,
        invoiceItemCellSelectionCheck,
        invoiceItemColumnVisibilityCheck,
        invoiceItemWorkbenchModeCheck,
        invoiceItemProductLibraryCheck,
        invoiceItemUndoRedoCheck,
        invoiceItemAutocompleteCheck,
        invoiceItemKeyboardNavigationCheck,
        profitButtonCheck,
        profitResultCheck,
        previewFrameCheck,
        draftInputCheck,
        draftPreviewFrameCheck,
        draftSavedOutputGuardCheck,
        draftRestoreCheck,
        savedOutputRestoredCheck,
        documentPackagePreviewFrameCheck,
        printButtonCheck,
        shippingMarkDesignerCheck,
        batchExportSettingsDeepLinkCheck: await invoiceDocumentOutputSmokeScene.runPackageSettingsDeepLink(page, options, timeoutMs),
        documentEmailSettingsDeepLinkCheck: await invoiceDocumentOutputSmokeScene.runDocumentEmailSettingsDeepLink(page, options, timeoutMs),
        productId: product.id,
        productCode: product.productCode,
        deleted,
        deletedProduct,
      };
    } finally {
      deletedProduct = await deleteSmokeProduct(options, accessToken, tokenType, product.id).catch(() => false);
      deleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      if (result) {
        result.deleted = deleted;
        result.deletedProduct = deletedProduct;
      }
    }
  
    return result;
  }

  return { run: waitForInvoiceReportCheck };
}
