import path from "node:path";

export function createPaymentSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    cloneJson,
    ensureTrailingSlash,
    evaluate,
    getApiSettings,
    getReportTemplates,
    includesText,
    normalizePathForCompare,
    redactDesktopAccessToken,
    saveApiSettings,
    setRecordValueKeepingExistingCase,
    smokeFileNameFromPath,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function run(page, options, accessToken, tokenType, timeoutMs) {
    const paymentReportCheck = await waitForPaymentReportCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
    );
    const paymentDeleteCheck = await waitForPaymentDeleteCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
    );

    return { paymentReportCheck, paymentDeleteCheck };
  }

  async function waitForPaymentDeleteCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.paymentDeleteCheck) {
      return null;
    }

    let payment = null;
    let detailStatus = null;
    let cleanupDeleted = false;
    let result = null;

    try {
      payment = await createSmokePayment(options, accessToken, tokenType);
      const checkUrl = buildPaymentDeleteCheckUrl(options.webUrl, payment.id);
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "基础信息",
        "业务信息",
        "金额和费用",
        "付款/报销单预览",
        "删除",
        payment.invoiceNo,
      ];

      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
      const deleteButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const toolbar = document.querySelector('[aria-label="编辑付款报销"] .editor-toolbar');
          const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('删除'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the payment delete button to become available.",
      );

      await evaluate(
        page,
        `(() => {
          window.__paymentDeleteConfirmMessages = [];
          window.confirm = (message) => {
            window.__paymentDeleteConfirmMessages.push(String(message || ''));
            return true;
          };

          const toolbar = document.querySelector('[aria-label="编辑付款报销"] .editor-toolbar');
          const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('删除'));
          if (!button || button.disabled) {
            throw new Error('Payment delete button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const deletedState = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => ({
            hash: window.location.hash || '',
            text: document.body ? document.body.innerText || '' : '',
            confirmMessages: Array.isArray(window.__paymentDeleteConfirmMessages)
              ? window.__paymentDeleteConfirmMessages.slice()
              : [],
          }))()`,
          true,
        ).catch(() => ({ value: null }));
        const value = state.value ?? {};
        const text = value.text || "";
        return value.hash.includes("/payments") &&
          !value.hash.includes(`/payments/${payment.id}`) &&
          text.includes("付款已删除")
          ? value
          : null;
      }, timeoutMs, () => `Timed out waiting for payment delete success message: ${payment.invoiceNo}`);

      const detailResponse = await fetch(new URL(`/api/payments/${payment.id}`, ensureTrailingSlash(options.apiBaseUrl)), {
        method: "GET",
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      detailStatus = detailResponse.status;
      if (detailStatus !== 404) {
        throw new Error(`Payment delete UI did not remove payment ${payment.id}; detail status was ${detailStatus}.`);
      }

      cleanupDeleted = true;
      result = {
        paymentId: payment.id,
        invoiceNo: payment.invoiceNo,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        deleteButtonCheck,
        confirmMessages: deletedState.confirmMessages,
        redirectedToList: deletedState.hash.includes("/payments") && !deletedState.hash.includes(`/payments/${payment.id}`),
        successMessageFound: includesText(deletedState.text || "", "付款已删除"),
        detailStatus,
        cleanupDeleted,
      };
    } finally {
      if (payment?.id && detailStatus !== 404) {
        cleanupDeleted = await deleteSmokePayment(options, accessToken, tokenType, payment.id).catch(() => false);
        if (result) {
          result.cleanupDeleted = cleanupDeleted;
        }
      }
    }

    return result;
  }

  async function waitForPaymentReportCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.paymentReportCheck) {
      return null;
    }

    const paymentTemplateSettings = await preparePaymentReportTemplateSettings(options, accessToken, tokenType);
    let payment = null;
    let result = null;
    let deleted = false;
    let restoredSettings = false;
    let restoreSettingsError = null;

    try {
      payment = await createSmokePayment(options, accessToken, tokenType);
      const checkUrl = buildPaymentReportCheckUrl(options.webUrl, payment.id);
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "基础信息",
        "金额和费用",
        "付款/报销单预览",
        "模板",
        "输出 PDF",
        "生成 PDF",
        "打印",
        "模板设置",
        payment.invoiceNo,
      ];

      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
      const keyboardFlowCheck = await waitForPaymentKeyboardFlowCheck(
        page,
        options,
        accessToken,
        tokenType,
        payment,
        timeoutMs,
      );
      const templateSettingsButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('模板设置'));
          return Boolean(button && !button.disabled && (button.title || '').includes('管理付款/报销模板'));
        })()`,
        timeoutMs,
        "Timed out waiting for the payment template settings button.",
      );

      const configuredTemplateSettingsCheck = await waitForPaymentTemplateSettingsAppliedCheck(
        page,
        paymentTemplateSettings,
        timeoutMs,
      );

      const previewButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('预览'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the payment report preview button to become available.",
      );

      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('预览'));
          if (!button || button.disabled) {
            throw new Error('Payment report preview button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const previewFrameCheck = await waitForPageExpression(
        page,
        `(() => {
          const frame = document.querySelector('iframe[title="付款/报销单 HTML 预览"]');
          const srcdoc = frame ? (frame.getAttribute('srcdoc') || frame.srcdoc || '') : '';
          return srcdoc.includes(${JSON.stringify(payment.invoiceNo)}) &&
            srcdoc.includes(${JSON.stringify(payment.payeeName)});
        })()`,
        timeoutMs,
        `Timed out waiting for payment report preview HTML: ${payment.invoiceNo}`,
      );

      const printButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('打印'));
          return Boolean(button && !button.disabled && (button.title || '').includes('打印当前预览'));
        })()`,
        timeoutMs,
        "Timed out waiting for the payment report print button to become available.",
      );

      const draftProjectValue = `Smoke Payment Draft Project ${payment.id}`;
      const paymentDraftOutputPath = path.join(options.userDataDir, `payment-draft-output-guard-${payment.id}.pdf`);
      await evaluate(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="编辑付款报销"]');
          if (!surface) {
            throw new Error('Payment editor surface not found for draft preview check.');
          }

          const notifyReactChange = (control) => {
            const reactPropsKey = Object.keys(control).find((key) => key.startsWith('__reactProps$'));
            const reactProps = reactPropsKey ? control[reactPropsKey] : null;
            if (reactProps && typeof reactProps.onChange === 'function') {
              reactProps.onChange({ target: control, currentTarget: control });
            }
          };
          const setNativeValue = (control, value) => {
            const prototype = Object.getPrototypeOf(control);
            const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
            if (descriptor && typeof descriptor.set === 'function') {
              descriptor.set.call(control, value);
            } else {
              control.value = value;
            }
            control.focus();
            control.dispatchEvent(new Event('input', { bubbles: true }));
            control.dispatchEvent(new Event('change', { bubbles: true }));
            notifyReactChange(control);
          };
          const fieldByLabel = (labelText) => {
            const labels = Array.from(surface.querySelectorAll('label'));
            const label = labels.find((item) =>
              Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
            if (!label) {
              throw new Error('Payment field not found: ' + labelText);
            }

            const control = label.querySelector('input, select, textarea');
            if (!control || control.disabled) {
              throw new Error('Payment field is not editable: ' + labelText);
            }

            return control;
          };
          const pathFieldByLabel = (labelText) => {
            const labels = Array.from(surface.querySelectorAll('.path-field-label'));
            const label = labels.find((item) => (item.textContent || '').trim() === labelText);
            if (!label || !label.id) {
              throw new Error('Payment path field label not found: ' + labelText);
            }

            const control = surface.querySelector(\`input[aria-labelledby="\${CSS.escape(label.id)}"]\`);
            if (!control || control.disabled) {
              throw new Error('Payment path field is not editable: ' + labelText);
            }

            return control;
          };

          setNativeValue(fieldByLabel('项目'), ${JSON.stringify(draftProjectValue)});
          setNativeValue(pathFieldByLabel('输出 PDF'), ${JSON.stringify(paymentDraftOutputPath)});
          return true;
        })()`,
        true,
      );

      const draftInputCheck = await waitForPageExpression(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="编辑付款报销"]');
          const labels = surface ? Array.from(surface.querySelectorAll('label')) : [];
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === '项目'));
          const input = label ? label.querySelector('input, select, textarea') : null;
          return Boolean(input && input.value === ${JSON.stringify(draftProjectValue)});
        })()`,
        timeoutMs,
        "Timed out waiting for the payment draft project field to update.",
      );

      const draftSavedOutputGuardCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const text = panel ? panel.innerText || '' : '';
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const pdfButton = buttons.find((element) => (element.innerText || '').includes('生成 PDF'));
          return Boolean(panel &&
            text.includes('当前付款/报销单有未保存修改') &&
            text.includes('请先保存后再生成') &&
            pdfButton &&
            pdfButton.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for payment saved-output guard while draft changes are unsaved.",
      );

      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('预览'));
          if (!button || button.disabled) {
            throw new Error('Payment draft report preview button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const draftPreviewFrameCheck = await waitForPageExpression(
        page,
        `(() => {
          const frame = document.querySelector('iframe[title="付款/报销单 HTML 预览"]');
          const srcdoc = frame ? (frame.getAttribute('srcdoc') || frame.srcdoc || '') : '';
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const text = panel ? panel.innerText || '' : '';
          return srcdoc.includes(${JSON.stringify(payment.invoiceNo)}) &&
            srcdoc.includes(${JSON.stringify(draftProjectValue)}) &&
            !srcdoc.includes(${JSON.stringify(keyboardFlowCheck.persistedProject)}) &&
            text.includes('付款/报销草稿 HTML 预览') &&
            text.includes('不按 Payment.InvoiceNo 读取发票/报关单据');
        })()`,
        timeoutMs,
        `Timed out waiting for payment draft report preview HTML: ${payment.invoiceNo}`,
      );

      await evaluate(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="编辑付款报销"]');
          if (!surface) {
            throw new Error('Payment editor surface not found for draft restore.');
          }

          const notifyReactChange = (control) => {
            const reactPropsKey = Object.keys(control).find((key) => key.startsWith('__reactProps$'));
            const reactProps = reactPropsKey ? control[reactPropsKey] : null;
            if (reactProps && typeof reactProps.onChange === 'function') {
              reactProps.onChange({ target: control, currentTarget: control });
            }
          };
          const setNativeValue = (control, value) => {
            const prototype = Object.getPrototypeOf(control);
            const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
            if (descriptor && typeof descriptor.set === 'function') {
              descriptor.set.call(control, value);
            } else {
              control.value = value;
            }
            control.focus();
            control.dispatchEvent(new Event('input', { bubbles: true }));
            control.dispatchEvent(new Event('change', { bubbles: true }));
            notifyReactChange(control);
          };
          const labels = Array.from(surface.querySelectorAll('label'));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === '项目'));
          const input = label ? label.querySelector('input, select, textarea') : null;
          if (!input || input.disabled) {
            throw new Error('Payment project field is not editable for restore.');
          }

          setNativeValue(input, ${JSON.stringify(keyboardFlowCheck.persistedProject)});
          return true;
        })()`,
        true,
      );

      const draftRestoreCheck = await waitForPageExpression(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="编辑付款报销"]');
          const labels = surface ? Array.from(surface.querySelectorAll('label')) : [];
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === '项目'));
          const input = label ? label.querySelector('input, select, textarea') : null;
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const text = panel ? panel.innerText || '' : '';
          return Boolean(input &&
            input.value === ${JSON.stringify(keyboardFlowCheck.persistedProject)} &&
            !text.includes('当前付款/报销单有未保存修改'));
        })()`,
        timeoutMs,
        "Timed out waiting for the payment project field to restore after draft preview check.",
      );

      const reimbursementTemplatePreviewCheck = await waitForPaymentExpenseReimbursementTemplatePreviewCheck(
        page,
        paymentTemplateSettings,
        payment,
        timeoutMs,
      );

      result = {
        paymentId: payment.id,
        invoiceNo: payment.invoiceNo,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        keyboardFlowCheck,
        templateSettingsButtonCheck,
        configuredTemplateSettingsCheck,
        previewButtonCheck,
        previewFrameCheck,
        printButtonCheck,
        draftInputCheck,
        draftSavedOutputGuardCheck,
        draftPreviewFrameCheck,
        draftRestoreCheck,
        reimbursementTemplatePreviewCheck,
        templateSettingsDeepLinkCheck: await waitForPaymentTemplateSettingsDeepLinkCheck(
          page,
          options,
          paymentTemplateSettings,
          timeoutMs,
        ),
        restoredSettings,
        deleted,
      };
    } finally {
      await saveApiSettings(options, accessToken, tokenType, paymentTemplateSettings.originalSettings)
        .then(() => {
          restoredSettings = true;
        })
        .catch((error) => {
          restoreSettingsError = error;
          if (result) {
            result.restoreSettingsError = error.message;
          }
        });

      deleted = payment?.id
        ? await deleteSmokePayment(options, accessToken, tokenType, payment.id).catch(() => false)
        : false;
      if (result) {
        result.restoredSettings = restoredSettings;
        result.deleted = deleted;
      }

      if (restoreSettingsError) {
        throw restoreSettingsError;
      }
    }

    return result;
  }

  async function waitForPaymentKeyboardFlowCheck(page, options, accessToken, tokenType, payment, timeoutMs) {
    const projectValue = `Tauri Payment CtrlS ${Date.now()}`;
    const enterFlowCheck = await waitForPageExpression(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑付款报销"]');
        if (!surface) {
          return false;
        }

        const fieldByLabel = (labelText) => {
          const labels = Array.from(surface.querySelectorAll('label'));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
          return label ? label.querySelector('input, select, textarea') : null;
        };

        const invoiceNo = fieldByLabel('发票号');
        const paymentDate = fieldByLabel('付款日期');
        if (!invoiceNo || !paymentDate) {
          return false;
        }

        invoiceNo.focus();
        invoiceNo.dispatchEvent(new KeyboardEvent('keydown', {
          key: 'Enter',
          bubbles: true,
          cancelable: true,
        }));
        return document.activeElement === paymentDate;
      })()`,
      timeoutMs,
      "Timed out waiting for payment editor Enter-as-Tab keyboard flow.",
    );

    await evaluate(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑付款报销"]');
        const notifyReactChange = (control) => {
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith('__reactProps$'));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === 'function') {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };
        const setNativeValue = (control, value) => {
          const prototype = Object.getPrototypeOf(control);
          const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
          if (descriptor && typeof descriptor.set === 'function') {
            descriptor.set.call(control, value);
          } else {
            control.value = value;
          }
          control.focus();
          control.dispatchEvent(new Event('input', { bubbles: true }));
          control.dispatchEvent(new Event('change', { bubbles: true }));
          notifyReactChange(control);
        };
        const fieldByLabel = (labelText) => {
          const labels = Array.from(surface.querySelectorAll('label'));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
          if (!label) {
            throw new Error('Payment field not found: ' + labelText);
          }

          const control = label.querySelector('input, select, textarea');
          if (!control) {
            throw new Error('Payment field has no editable control: ' + labelText);
          }

          return control;
        };

        setNativeValue(fieldByLabel('项目'), ${JSON.stringify(projectValue)});
        return true;
      })()`,
      true,
    );

    await evaluate(
      page,
      `(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', {
          key: 's',
          ctrlKey: true,
          bubbles: true,
          cancelable: true,
        }));
        return true;
      })()`,
      true,
    );

    const saveUiCheck = await waitForPageExpression(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑付款报销"]');
        const text = surface ? surface.innerText || '' : '';
        return text.includes('付款报销已保存');
      })()`,
      timeoutMs,
      "Timed out waiting for payment Ctrl+S save success message.",
    );

    const savedPayment = await waitFor(async () => {
      const response = await fetch(new URL(`/api/payments/${payment.id}`, ensureTrailingSlash(options.apiBaseUrl)), {
        method: "GET",
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!response.ok) {
        return null;
      }

      const payload = await response.json();
      return payload?.project === projectValue ? payload : null;
    }, timeoutMs, () => `Timed out waiting for payment Ctrl+S persisted project: ${payment.invoiceNo}`);

    return {
      enterFlowCheck,
      ctrlSSaveUiCheck: saveUiCheck,
      persistedProject: savedPayment.project,
    };
  }

  async function waitForPaymentTemplateSettingsDeepLinkCheck(page, options, settings, timeoutMs) {
    const checkUrl = buildPaymentTemplateSettingsDeepLinkUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "设置",
      "付款/报销模板设置",
      "付款/报销模板",
      "新增模板",
    ];
    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const panelCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="付款/报销模板设置"]');
        if (!panel || !window.location.hash.includes('/settings?section=paymentTemplates')) {
          return false;
        }

        const rect = panel.getBoundingClientRect();
        return rect.bottom > 0 && rect.top < Math.max(120, window.innerHeight * 0.75);
      })()`,
      timeoutMs,
      "Timed out waiting for the payment template settings deep link to focus the panel.",
    );

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      panelCheck,
    };
  }

  async function preparePaymentReportTemplateSettings(options, accessToken, tokenType) {
    const [settingsResponse, templates] = await Promise.all([
      getApiSettings(options, accessToken, tokenType),
      getReportTemplates(options, accessToken, tokenType, "PaymentVoucher"),
    ]);
    const originalSettings = cloneJson(settingsResponse.settings ?? settingsResponse.Settings);
    if (!originalSettings || Object.keys(originalSettings).length === 0) {
      throw new Error("Payment report smoke could not read settings for template compatibility check.");
    }

    if (!Array.isArray(templates) || templates.length === 0) {
      throw new Error("Payment report smoke requires at least one PaymentVoucher template.");
    }

    const preferredTemplate =
      templates.find((template) => smokeFileNameFromPath(template?.templatePath).toLowerCase() === "payment_voucher_template.html") ??
      templates[0];
    const reimbursementTemplate =
      templates.find((template) => smokeFileNameFromPath(template?.templatePath).toLowerCase() === "expense_reimbursement_template.html") ??
      null;
    const disabledTemplate =
      templates.find(
        (template) =>
          normalizePathForCompare(template?.templatePath) !== normalizePathForCompare(preferredTemplate?.templatePath) &&
          (!reimbursementTemplate ||
            normalizePathForCompare(template?.templatePath) !== normalizePathForCompare(reimbursementTemplate?.templatePath)),
      ) ?? null;
    const preferredName = "Smoke Payment Preferred Template";
    const reimbursementName = "Smoke Expense Reimbursement Template";
    const disabledName = "Smoke Payment Disabled Template";
    const smokeSettings = cloneJson(originalSettings);
    const paymentTemplates = [
      {
        name: preferredName,
        templatePath: preferredTemplate.templatePath,
        reportType: "PaymentDocument",
        isEnabled: true,
        showSeal: false,
      },
    ];

    if (reimbursementTemplate) {
      paymentTemplates.push({
        name: reimbursementName,
        templatePath: reimbursementTemplate.templatePath,
        reportType: "PaymentDocument",
        isEnabled: true,
        showSeal: false,
      });
    }

    if (disabledTemplate) {
      paymentTemplates.push({
        name: disabledName,
        templatePath: disabledTemplate.templatePath,
        reportType: "PaymentDocument",
        isEnabled: false,
        showSeal: true,
      });
    }

    setRecordValueKeepingExistingCase(smokeSettings, ["paymentTemplates", "PaymentTemplates"], paymentTemplates);
    await saveApiSettings(options, accessToken, tokenType, smokeSettings);

    return {
      originalSettings,
      preferredName,
      preferredPath: preferredTemplate.templatePath,
      reimbursementName: reimbursementTemplate ? reimbursementName : "",
      reimbursementPath: reimbursementTemplate?.templatePath ?? "",
      disabledName: disabledTemplate ? disabledName : "",
      disabledPath: disabledTemplate?.templatePath ?? "",
      reportTypeAlias: "PaymentDocument",
    };
  }

  async function waitForPaymentTemplateSettingsAppliedCheck(page, settings, timeoutMs) {
    let latestState = null;
    return waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const select = panel ? panel.querySelector('select') : null;
          const options = select ? Array.from(select.options).map((option) => ({
            text: (option.textContent || '').trim(),
            value: option.value || '',
          })) : [];
          const sealCheckbox = panel ? panel.querySelector('.toggle-field input[type="checkbox"]') : null;
          const preferredName = ${JSON.stringify(settings.preferredName)};
          const preferredPath = ${JSON.stringify(settings.preferredPath)};
          const reimbursementName = ${JSON.stringify(settings.reimbursementName)};
          const reimbursementPath = ${JSON.stringify(settings.reimbursementPath)};
          const disabledName = ${JSON.stringify(settings.disabledName)};
          const disabledPath = ${JSON.stringify(settings.disabledPath)};
          const preferredVisible = options.some((option) => option.text.includes(preferredName) && option.value === preferredPath);
          const reimbursementVisible = !reimbursementPath ||
            options.some((option) => option.text.includes(reimbursementName) && option.value === reimbursementPath);
          const disabledLabelHidden = !disabledName || !options.some((option) => option.text.includes(disabledName));
          const disabledPathHidden = !disabledPath || !options.some((option) => option.value === disabledPath);
          const selectedPreferred = Boolean(select && select.value === preferredPath);
          const sealDefaultApplied = Boolean(sealCheckbox && sealCheckbox.checked === false);
          return {
            found: Boolean(preferredVisible && reimbursementVisible && disabledLabelHidden && disabledPathHidden && selectedPreferred && sealDefaultApplied),
            preferredVisible,
            reimbursementVisible,
            disabledLabelHidden,
            disabledPathHidden,
            selectedPreferred,
            sealDefaultApplied,
            selectedValue: select ? select.value : '',
            options,
            withSeal: sealCheckbox ? sealCheckbox.checked : null,
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: error.message } }));

      latestState = result.value ?? null;
      return latestState?.found ? latestState : null;
    }, timeoutMs, () => `Timed out waiting for payment template settings to apply to the payment report panel: ${JSON.stringify(latestState)}`);
  }

  async function waitForPaymentExpenseReimbursementTemplatePreviewCheck(page, settings, payment, timeoutMs) {
    if (!settings.reimbursementPath) {
      return {
        skipped: true,
        reason: "No expense_reimbursement_template.html was available in the PaymentVoucher template catalog.",
      };
    }

    await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="付款/报销单预览"]');
        const select = panel ? panel.querySelector('select') : null;
        if (!select || select.disabled) {
          throw new Error('Payment report template select is not available.');
        }

        const targetValue = ${JSON.stringify(settings.reimbursementPath)};
        const option = Array.from(select.options).find((item) => item.value === targetValue);
        if (!option) {
          throw new Error('Expense reimbursement template option is missing.');
        }

        const valueSetter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set;
        if (valueSetter) {
          valueSetter.call(select, targetValue);
        } else {
          select.value = targetValue;
        }
        select.dispatchEvent(new Event('input', { bubbles: true }));
        select.dispatchEvent(new Event('change', { bubbles: true }));
        const reactPropsKey = Object.keys(select).find((key) => key.startsWith('__reactProps$'));
        const reactProps = reactPropsKey ? select[reactPropsKey] : null;
        if (reactProps && typeof reactProps.onChange === 'function') {
          reactProps.onChange({ target: select, currentTarget: select });
        }

        return select.value;
      })()`,
      true,
    );

    const selectedTemplateCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="付款/报销单预览"]');
        const select = panel ? panel.querySelector('select') : null;
        const sealCheckbox = panel ? panel.querySelector('.toggle-field input[type="checkbox"]') : null;
        const frame = panel ? panel.querySelector('iframe[title="付款/报销单 HTML 预览"]') : null;
        const empty = panel ? panel.querySelector('.report-preview-empty') : null;
        return Boolean(select &&
          select.value === ${JSON.stringify(settings.reimbursementPath)} &&
          panel.getAttribute('data-selected-template-path') === ${JSON.stringify(settings.reimbursementPath)} &&
          !frame &&
          empty &&
          (empty.innerText || '').includes('暂无预览') &&
          sealCheckbox &&
          sealCheckbox.checked === false);
      })()`,
      timeoutMs,
      "Timed out waiting for expense reimbursement template selection.",
    );

    await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="付款/报销单预览"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const button = buttons.find((element) => (element.innerText || '').includes('预览'));
        if (!button || button.disabled) {
          throw new Error('Payment expense reimbursement preview button is not available.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    let latestFrameState = null;
    const frameCheck = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const normalize = (value) => String(value || '').replace(/\\\\/g, '/').replace(/\\/+/g, '/').toLowerCase();
          const panel = document.querySelector('[aria-label="付款/报销单预览"]');
          const frame = panel ? panel.querySelector('iframe[title="付款/报销单 HTML 预览"]') : null;
          const srcdoc = frame ? (frame.getAttribute('srcdoc') || frame.srcdoc || '') : '';
          const selectedTemplatePath = panel ? panel.getAttribute('data-selected-template-path') || '' : '';
          const previewTemplatePath = panel ? panel.getAttribute('data-preview-template-path') || '' : '';
          const frameTemplatePath = frame ? frame.getAttribute('data-template-path') || '' : '';
          const alertText = panel
            ? Array.from(panel.querySelectorAll('.alert')).map((item) => (item.innerText || '').trim()).filter(Boolean).join('\\n')
            : '';
          const emptyText = panel?.querySelector('.report-preview-empty')?.innerText || '';
          const targetPath = ${JSON.stringify(settings.reimbursementPath)};
          const checks = {
            selectedTemplateMatches: normalize(selectedTemplatePath) === normalize(targetPath),
            previewTemplateMatches: normalize(previewTemplatePath) === normalize(targetPath),
            frameTemplateMatches: normalize(frameTemplatePath) === normalize(targetPath),
            hasTitle: srcdoc.includes('费用报销明细单'),
            hasPayerName: srcdoc.includes(${JSON.stringify(payment.payerName)}),
            hasDepartment: srcdoc.includes(${JSON.stringify(payment.department)}),
            hasNotes: srcdoc.includes(${JSON.stringify(payment.notes)}),
            hasTravelExpense: srcdoc.includes('11.11'),
            hasCnyTotal: srcdoc.includes('88.88'),
            excludesPayeeName: !srcdoc.includes(${JSON.stringify(payment.payeeName)}),
          };
          const found = Boolean(
            checks.selectedTemplateMatches &&
            checks.previewTemplateMatches &&
            checks.frameTemplateMatches &&
            checks.hasTitle &&
            checks.hasPayerName &&
            checks.hasDepartment &&
            checks.hasNotes &&
            checks.hasTravelExpense &&
            checks.hasCnyTotal &&
            checks.excludesPayeeName
          );

          return {
            found,
            checks,
            selectedTemplatePath,
            previewTemplatePath,
            frameTemplatePath,
            srcdocLength: srcdoc.length,
            srcdocExcerpt: srcdoc.slice(0, 1200),
            alertText,
            emptyText,
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: error.message } }));

      latestFrameState = result.value ?? null;
      return latestFrameState?.found ? latestFrameState : null;
    }, timeoutMs, () => `Timed out waiting for expense reimbursement template preview HTML: ${JSON.stringify(latestFrameState)}`);

    return {
      skipped: false,
      selectedTemplateCheck,
      frameCheck,
      templatePath: settings.reimbursementPath,
      templateName: settings.reimbursementName,
    };
  }

  async function createSmokePayment(options, accessToken, tokenType) {
    const timestamp = Date.now();
    const invoiceNo = `SMOKE-PAY-${timestamp}`;
    const paymentDate = `${new Date().toISOString().slice(0, 10)}T00:00:00`;
    const body = {
      id: 0,
      invoiceNo,
      shipmentDate: paymentDate,
      payeeName: `Smoke Payee ${timestamp}`,
      payerName: "Smoke Payer",
      paymentDate,
      receiptDate: paymentDate,
      usdAmount: 12.34,
      cnyAmount: 88.88,
      paymentMethod: "Bank Transfer",
      department: "Smoke Department",
      project: "Tauri Payment Report Smoke",
      goodsName: "Smoke Goods",
      quantity: "1",
      bankName: "Smoke Bank",
      accountNo: "SMOKE-ACCOUNT",
      notes: "Created by Tauri payment report smoke and deleted after verification.",
      travelExpense: 11.11,
      businessEntertainmentExpense: 22.22,
      telephoneExpense: 3.33,
      officeExpense: 4.44,
      repairExpense: 5.55,
      freightMiscExpense: 6.66,
      inspectionExpense: 7.77,
      otherExpense: 27.8,
    };

    const response = await fetch(new URL("/api/payments", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(`Payment smoke create failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.id || !payload?.payment) {
      throw new Error(`Payment smoke create response did not include id/payment: ${JSON.stringify(payload)}`);
    }

    return payload.payment;
  }

  async function deleteSmokePayment(options, accessToken, tokenType, paymentId) {
    const response = await fetch(new URL(`/api/payments/${paymentId}`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "DELETE",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });

    return response.ok;
  }

  function buildPaymentDeleteCheckUrl(webUrl, paymentId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokePaymentDelete", String(paymentId));
    url.hash = `/payments/${paymentId}`;
    return url.toString();
  }

  function buildPaymentReportCheckUrl(webUrl, paymentId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokePaymentReport", String(paymentId));
    url.hash = `/payments/${paymentId}`;
    return url.toString();
  }

  function buildPaymentTemplateSettingsDeepLinkUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokePaymentTemplateSettings", "1");
    url.hash = "/settings?section=paymentTemplates";
    return url.toString();
  }

  return { run };
}
