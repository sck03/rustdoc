import { existsSync, rmSync } from "node:fs";
import path from "node:path";

export function createSettingsBackupSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    buildBatchExportSettingsDeepLinkUrl,
    buildDocumentEmailSettingsDeepLinkUrl,
    buildSettingsSectionUrl,
    ensureTrailingSlash,
    evaluate,
    includesText,
    isPathInsideRoot,
    normalizePathForCompare,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function runPreparation(page, options, timeoutMs) {
    const backupCheck = await waitForBackupCheck(page, options, timeoutMs);
    const backupCreateCheck = await waitForBackupCreateCheck(page, options, timeoutMs);
    return { backupCheck, backupCreateCheck };
  }

  async function waitForBackupCheck(page, options, timeoutMs) {
    if (!options.backupCheck) {
      return null;
    }

    const runtimeUrl = buildSettingsSectionUrl(options.webUrl, "system", "smokeRuntimeSettings");
    await page.send("Page.navigate", { url: runtimeUrl });
    const runtimeExpectedText = [
      "设置",
      "运行与数据库",
      "默认导出目录",
    ];
    const runtimePageText = await waitForRuntimeDiagnostics(page, runtimeExpectedText, timeoutMs);
    const defaultExportDirectoryPickerCheck = options.mockTauriRuntimeContext
      ? await waitForDefaultExportDirectoryPickerCheck(page, options, timeoutMs)
      : null;

    const templateUrl = buildBatchExportSettingsDeepLinkUrl(options.webUrl);
    await page.send("Page.navigate", { url: templateUrl });
    const templateExpectedText = [
      "设置",
      "模板设置",
      "单证模板设置",
      "文件命名规则",
      "文件夹命名规则",
      "默认合并 PDF",
      "默认生成 ZIP",
      "新增单证",
      "导出项",
      "付款/报销模板设置",
      "新增模板",
      "付款/报销模板",
    ];
    const templatePageText = await waitForRuntimeDiagnostics(page, templateExpectedText, timeoutMs);

    const batchExportSettingsCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单证模板设置"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const rows = panel ? Array.from(panel.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell')) : [];
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          panel.querySelector('.batch-export-items-toolbar') &&
          panel.querySelector('.batch-export-items-table') &&
          panel.querySelector('input[type="checkbox"]') &&
          panel.querySelector('input') &&
          text.includes('文件命名规则') &&
          text.includes('文件夹命名规则') &&
          text.includes('默认合并 PDF') &&
          text.includes('默认生成 ZIP') &&
          (rows.length === 0 || buttons.some((button) => (button.title || '').includes('选择模板文件'))) &&
          buttons.some((button) => (button.innerText || '').includes('新增单证')));
      })()`,
      timeoutMs,
      "Timed out waiting for the batch export settings panel.",
    );

    const batchExportOrderInteractionCheck = await waitForBatchExportOrderInteractionCheck(page, timeoutMs);

    const paymentTemplateSettingsCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="付款/报销模板设置"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const rows = panel ? Array.from(panel.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell')) : [];
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          panel.querySelector('.batch-export-items-toolbar') &&
          panel.querySelector('[aria-label="付款/报销模板"]') &&
          text.includes('付款/报销模板') &&
          (rows.length === 0 || buttons.some((button) => (button.title || '').includes('选择模板文件'))) &&
          buttons.some((button) => (button.innerText || '').includes('新增模板')));
      })()`,
      timeoutMs,
      "Timed out waiting for the payment template settings panel.",
    );

    const paymentTemplateOrderInteractionCheck = await waitForPaymentTemplateOrderInteractionCheck(page, timeoutMs);

    const excelUrl = buildSettingsSectionUrl(options.webUrl, "excelImport", "smokeExcelImportSettings");
    await page.send("Page.navigate", { url: excelUrl });
    const excelExpectedText = [
      "设置",
      "Excel 导入",
      "Excel 导入方案",
      "当前方案名",
      "已有方案",
      "加载方案",
      "保存方案",
      "出口商中文",
      "发票号",
      "明细起始行",
      "总价列",
    ];
    const excelPageText = await waitForRuntimeDiagnostics(page, excelExpectedText, timeoutMs);

    const excelImportSettingsCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="Excel 导入方案"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          panel.querySelector('.excel-import-field-group') &&
          panel.querySelector('input') &&
          panel.querySelector('select') &&
          text.includes('当前方案名') &&
          text.includes('已有方案') &&
          text.includes('出口商中文') &&
          text.includes('客户名称') &&
          text.includes('发票号') &&
          text.includes('明细起始行') &&
          text.includes('总价列') &&
          buttons.some((button) => (button.innerText || '').includes('加载方案')) &&
          buttons.some((button) => (button.innerText || '').includes('保存方案')));
      })()`,
      timeoutMs,
      "Timed out waiting for the Excel import scheme settings panel.",
    );

    const exchangeRateUrl = buildSettingsSectionUrl(options.webUrl, "exchangeRate", "smokeExchangeRateSettings");
    await page.send("Page.navigate", { url: exchangeRateUrl });
    const exchangeRateExpectedText = [
      "设置",
      "汇率与币制",
      "汇率源网址",
      "缓存分钟",
      "常用货币",
      "更新货币列表",
    ];
    const exchangeRatePageText = await waitForRuntimeDiagnostics(page, exchangeRateExpectedText, timeoutMs);
    const exchangeRateSettingsCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="汇率与币制"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          panel.querySelector('.exchange-currency-settings-panel') &&
          text.includes('汇率源网址') &&
          text.includes('缓存分钟') &&
          text.includes('常用货币') &&
          buttons.some((button) => (button.innerText || '').includes('更新货币列表')));
      })()`,
      timeoutMs,
      "Timed out waiting for the exchange rate and currency settings panel.",
    );

    const singleWindowUrl = buildSettingsSectionUrl(options.webUrl, "singleWindow", "smokeSingleWindowSettings");
    await page.send("Page.navigate", { url: singleWindowUrl });
    const singleWindowExpectedText = [
      "设置",
      "AI 与单一窗口",
      "AI 设置",
      "AI API 地址",
      "AI 模型",
      "单一窗口默认值",
      "签证机构代码(4位)",
      "领证机构代码(4位)",
    ];
    const singleWindowPageText = await waitForRuntimeDiagnostics(page, singleWindowExpectedText, timeoutMs);

    const communicationUrl = buildDocumentEmailSettingsDeepLinkUrl(options.webUrl);
    await page.send("Page.navigate", { url: communicationUrl });
    const communicationExpectedText = [
      "设置",
      "邮件与备份",
      "推断 SMTP",
      "测试邮件连接",
      "单据邮件主题",
      "单据邮件正文",
      "数据备份与还原",
      "备份目录",
      "Backups",
      "创建备份",
      "保留天数",
      "清理旧备份",
      "还原备份",
      "确认文本",
      "还原数据库",
      "文件",
      "大小",
      "路径",
    ];

    const communicationPageText = await waitForRuntimeDiagnostics(page, communicationExpectedText, timeoutMs);
    const backupPanelCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="数据备份与还原"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        return Boolean(panel &&
          panel.querySelector('.backup-action-grid') &&
          panel.querySelector('.backup-table') &&
          panel.querySelector('input[placeholder="RESTORE"]') &&
          (panel.innerText || '').includes('Backups') &&
          buttons.some((button) => (button.innerText || '').includes('创建备份')) &&
          buttons.some((button) => (button.innerText || '').includes('清理旧备份')) &&
          buttons.some((button) => (button.innerText || '').includes('还原数据库')));
      })()`,
      timeoutMs,
      "Timed out waiting for the backup management panel.",
    );

    const documentEmailServerSuggestionCheck = await waitForDocumentEmailServerSuggestionCheck(page, timeoutMs);

    const documentEmailSettingsCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="邮件与备份"]');
        const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          panel.querySelector('textarea') &&
          buttons.some((button) => (button.innerText || '').includes('推断 SMTP')) &&
          buttons.some((button) => (button.innerText || '').includes('测试邮件连接')) &&
          text.includes('单据邮件主题') &&
          text.includes('单据邮件正文') &&
          text.includes('SMTP 服务器'));
      })()`,
      timeoutMs,
      "Timed out waiting for the document email default settings.",
    );

    return {
      url: redactDesktopAccessToken(runtimeUrl),
      sectionUrls: {
        runtime: redactDesktopAccessToken(runtimeUrl),
        templates: redactDesktopAccessToken(templateUrl),
        excelImport: redactDesktopAccessToken(excelUrl),
        exchangeRate: redactDesktopAccessToken(exchangeRateUrl),
        singleWindow: redactDesktopAccessToken(singleWindowUrl),
        communication: redactDesktopAccessToken(communicationUrl),
      },
      expectedText: [
        ...runtimeExpectedText.map((value) => ({ section: "runtime", value, found: includesText(runtimePageText, value) })),
        ...templateExpectedText.map((value) => ({ section: "templates", value, found: includesText(templatePageText, value) })),
        ...excelExpectedText.map((value) => ({ section: "excelImport", value, found: includesText(excelPageText, value) })),
        ...exchangeRateExpectedText.map((value) => ({ section: "exchangeRate", value, found: includesText(exchangeRatePageText, value) })),
        ...singleWindowExpectedText.map((value) => ({ section: "singleWindow", value, found: includesText(singleWindowPageText, value) })),
        ...communicationExpectedText.map((value) => ({ section: "communication", value, found: includesText(communicationPageText, value) })),
      ],
      backupPanelCheck,
      batchExportSettingsCheck,
      batchExportOrderInteractionCheck,
      defaultExportDirectoryPickerCheck,
      excelImportSettingsCheck,
      exchangeRateSettingsCheck,
      paymentTemplateSettingsCheck,
      paymentTemplateOrderInteractionCheck,
      documentEmailServerSuggestionCheck,
      documentEmailSettingsCheck,
      textExcerpt: [
        runtimePageText,
        templatePageText,
        excelPageText,
        exchangeRatePageText,
        communicationPageText,
      ].join("\n---\n").slice(0, 1200),
    };
  }

  async function waitForDefaultExportDirectoryPickerCheck(page, options, timeoutMs) {
    const expectedPath = path.join(options.userDataDir, "DefaultExportDirectorySmoke");
    const buttonReady = await waitForPageExpression(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="系统与数据库"]');
        const label = section
          ? Array.from(section.querySelectorAll('label')).find((item) => (item.innerText || '').includes('默认导出目录'))
          : null;
        const button = label ? label.querySelector('button[title*="选择默认导出目录"]') : null;
        return Boolean(label && button && !button.disabled);
      })()`,
      timeoutMs,
      "Timed out waiting for the default export directory picker button.",
    );

    const clickResult = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="系统与数据库"]');
        const label = section
          ? Array.from(section.querySelectorAll('label')).find((item) => (item.innerText || '').includes('默认导出目录'))
          : null;
        const button = label ? label.querySelector('button[title*="选择默认导出目录"]') : null;
        if (!button || button.disabled) {
          throw new Error('默认导出目录选择按钮不可用');
        }

        button.click();
        return { clicked: true };
      })()`,
      true,
    );

    const applied = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const expectedPath = ${JSON.stringify(expectedPath)};
          const section = document.querySelector('[aria-label="系统与数据库"]');
          const label = section
            ? Array.from(section.querySelectorAll('label')).find((item) => (item.innerText || '').includes('默认导出目录'))
            : null;
          const input = label ? label.querySelector('input') : null;
          const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
          const invocationFound = invocations.some((item) => item.command === 'select_directory');
          return {
            inputValue: input ? input.value : '',
            invocationFound,
            invocationCount: invocations.filter((item) => item.command === 'select_directory').length,
            matched: Boolean(input && input.value === expectedPath && invocationFound),
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));

      return result.value?.matched ? result.value : null;
    }, timeoutMs, "Timed out waiting for the default export directory picker to update the settings draft.");

    return {
      expectedPath,
      buttonReady,
      clicked: Boolean(clickResult.value?.clicked),
      applied,
    };
  }

  async function waitForDocumentEmailServerSuggestionCheck(page, timeoutMs) {
    let latestDraftState = null;
    let latestAppliedState = null;

    const draftReady = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="邮件与备份"]');
          if (!panel) {
            return { found: false, reason: 'missing email panel' };
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
            const labels = Array.from(panel.querySelectorAll('label'));
            const label = labels.find((item) =>
              Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
            if (!label) {
              throw new Error('字段未找到: ' + labelText);
            }

            const control = label.querySelector('input, select, textarea');
            if (!control) {
              throw new Error('字段没有可编辑控件: ' + labelText);
            }

            return control;
          };

          const fromAddress = fieldByLabel('发件人地址');
          const userName = fieldByLabel('邮箱账号');
          const smtpHost = fieldByLabel('SMTP 服务器');
          const smtpPort = fieldByLabel('SMTP 端口');
          const enableSsl = fieldByLabel('启用 SSL');
          const button = Array.from(panel.querySelectorAll('button')).find((candidate) => (candidate.innerText || '').includes('推断 SMTP'));
          if (!button) {
            return { found: false, reason: 'missing suggestion button' };
          }

          setNativeValue(fromAddress, 'user@qq.com');
          setNativeValue(userName, '');
          setNativeValue(smtpHost, '');
          setNativeValue(smtpPort, '0');
          if (enableSsl.checked) {
            enableSsl.click();
          }

          return {
            found: Boolean(
              (fromAddress.value || '') === 'user@qq.com' &&
              (userName.value || '') === '' &&
              (smtpHost.value || '') === '' &&
              Number(smtpPort.value || 0) === 0 &&
              enableSsl.checked === false &&
              !button.disabled
            ),
            fromAddress: fromAddress.value || '',
            userName: userName.value || '',
            smtpHost: smtpHost.value || '',
            smtpPort: smtpPort.value || '',
            enableSsl: enableSsl.checked,
            buttonDisabled: button.disabled,
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: error.message } }));

      latestDraftState = result.value ?? null;
      return latestDraftState?.found ? latestDraftState : null;
    }, timeoutMs, () => `Timed out waiting for the email server suggestion draft state: ${JSON.stringify(latestDraftState)}`);

    const clickResult = await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="邮件与备份"]');
        if (!panel) {
          return { found: false, reason: 'missing email panel' };
        }

        const button = Array.from(panel.querySelectorAll('button')).find((candidate) => (candidate.innerText || '').includes('推断 SMTP'));
        if (!button || button.disabled) {
          return { found: false, reason: 'missing enabled suggestion button' };
        }

        button.click();
        return {
          found: true,
          title: button.title || button.getAttribute('aria-label') || '',
          text: button.innerText || ''
        };
      })()`,
      true,
    ).catch((error) => ({ value: { found: false, error: error.message } }));

    const appliedReady = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="邮件与备份"]');
          if (!panel) {
            return { found: false, reason: 'missing email panel' };
          }

          const fieldByLabel = (labelText) => {
            const labels = Array.from(panel.querySelectorAll('label'));
            const label = labels.find((item) =>
              Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
            if (!label) {
              throw new Error('字段未找到: ' + labelText);
            }

            const control = label.querySelector('input, select, textarea');
            if (!control) {
              throw new Error('字段没有可编辑控件: ' + labelText);
            }

            return control;
          };

          const fromAddress = fieldByLabel('发件人地址');
          const userName = fieldByLabel('邮箱账号');
          const smtpHost = fieldByLabel('SMTP 服务器');
          const smtpPort = fieldByLabel('SMTP 端口');
          const enableSsl = fieldByLabel('启用 SSL');
          return {
            found:
              (fromAddress.value || '') === 'user@qq.com' &&
              (userName.value || '') === 'user@qq.com' &&
              (smtpHost.value || '') === 'smtp.qq.com' &&
              Number(smtpPort.value || 0) === 465 &&
              enableSsl.checked === true,
            fromAddress: fromAddress.value || '',
            userName: userName.value || '',
            smtpHost: smtpHost.value || '',
            smtpPort: smtpPort.value || '',
            enableSsl: enableSsl.checked,
            buttonText: Array.from(panel.querySelectorAll('button'))
              .find((candidate) => (candidate.innerText || '').includes('推断 SMTP'))?.innerText || ''
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: error.message } }));

      latestAppliedState = result.value ?? null;
      return latestAppliedState?.found ? latestAppliedState : null;
    }, timeoutMs, () => `Timed out waiting for the email server suggestion to apply: ${JSON.stringify({ draft: latestDraftState, clickResult: clickResult.value ?? null, applied: latestAppliedState })}`);

    return {
      draftReady,
      clickResult: clickResult.value ?? null,
      appliedReady,
    };
  }

  async function waitForBatchExportOrderInteractionCheck(page, timeoutMs) {
    return waitForTemplateOrderInteractionCheck(page, timeoutMs, {
      panelLabel: "单证模板设置",
      addButtonText: "新增单证",
      firstName: "Smoke Batch Export Alpha",
      secondName: "Smoke Batch Export Beta",
      description: "batch export item",
    });
  }

  async function waitForPaymentTemplateOrderInteractionCheck(page, timeoutMs) {
    return waitForTemplateOrderInteractionCheck(page, timeoutMs, {
      panelLabel: "付款/报销模板设置",
      addButtonText: "新增模板",
      firstName: "Smoke Payment Template Alpha",
      secondName: "Smoke Payment Template Beta",
      description: "payment template",
    });
  }

  async function waitForTemplateOrderInteractionCheck(page, timeoutMs, config) {
    const panelSelector = JSON.stringify(`[aria-label="${config.panelLabel}"]`);
    const addButtonText = JSON.stringify(config.addButtonText);
    const firstNameText = JSON.stringify(config.firstName);
    const secondNameText = JSON.stringify(config.secondName);
    const description = config.description;

    const setup = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector(${panelSelector});
          if (!panel) {
            return null;
          }

          const rows = Array.from(panel.querySelectorAll('tbody tr'))
            .filter((row) => !row.querySelector('.empty-cell'));
          if (rows.length >= 2) {
            return { rowCount: rows.length, requestedAdd: false };
          }

          const addButton = Array.from(panel.querySelectorAll('button'))
            .find((button) => (button.innerText || '').includes(${addButtonText}));
          if (!addButton || addButton.disabled) {
            return null;
          }

          addButton.click();
          addButton.click();
          return { rowCount: rows.length, requestedAdd: true };
        })()`,
        true,
      ).catch(() => ({ value: null }));

      return result.value && result.value.rowCount >= 2 ? result.value : null;
    }, timeoutMs, `Timed out preparing ${description}s for order interaction.`);

    const before = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector(${panelSelector});
          if (!panel) {
            return null;
          }

          const rows = Array.from(panel.querySelectorAll('tbody tr'))
            .filter((row) => !row.querySelector('.empty-cell'));
          if (rows.length < 2) {
            return null;
          }

          const setInputValue = (input, value) => {
            const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
            if (setter) {
              setter.call(input, value);
            } else {
              input.value = value;
            }
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
          };
          const readNameInput = (row) => row.querySelector('input.batch-export-cell-input:not(.batch-export-path-input)');
          const firstName = readNameInput(rows[0]);
          const secondName = readNameInput(rows[1]);
          if (!firstName || !secondName || firstName.disabled || secondName.disabled) {
            return null;
          }

          setInputValue(firstName, ${firstNameText});
          setInputValue(secondName, ${secondNameText});
          return {
            names: Array.from(panel.querySelectorAll('tbody tr'))
              .filter((row) => !row.querySelector('.empty-cell'))
              .slice(0, 2)
              .map((row) => readNameInput(row)?.value || ''),
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));

      const names = result.value?.names ?? [];
      return names[0] === config.firstName && names[1] === config.secondName
        ? names
        : null;
    }, timeoutMs, `Timed out assigning sentinel names to ${description}s.`);

    const moveAction = await evaluate(
      page,
      `(() => {
        const panel = document.querySelector(${panelSelector});
        const rows = panel
          ? Array.from(panel.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
          : [];
        if (rows.length < 2) {
          return { clicked: false, reason: 'not-enough-rows' };
        }

        const downButton = Array.from(rows[0].querySelectorAll('button'))
          .find((button) => (button.title || '').includes('下移'));
        if (!downButton || downButton.disabled) {
          return { clicked: false, reason: 'down-button-disabled' };
        }

        downButton.click();
        return { clicked: true };
      })()`,
      true,
    );

    if (!moveAction.value?.clicked) {
      throw new Error(`Failed to click ${description} move-down action: ${JSON.stringify(moveAction.value)}`);
    }

    const after = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector(${panelSelector});
          if (!panel) {
            return null;
          }

          const readNameInput = (row) => row.querySelector('input.batch-export-cell-input:not(.batch-export-path-input)');
          return Array.from(panel.querySelectorAll('tbody tr'))
            .filter((row) => !row.querySelector('.empty-cell'))
            .slice(0, 2)
            .map((row) => readNameInput(row)?.value || '');
        })()`,
        true,
      ).catch(() => ({ value: null }));

      const names = result.value ?? [];
      return names[0] === config.secondName && names[1] === config.firstName
        ? names
        : null;
    }, timeoutMs, `Timed out waiting for ${description} order to change after move-down.`);

    return {
      panelLabel: config.panelLabel,
      setup,
      before,
      after,
      moved: true,
    };
  }

  async function waitForBackupCreateCheck(page, options, timeoutMs) {
    if (!options.backupCreateCheck) {
      return null;
    }

    const checkUrl = buildBackupCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    await waitForPageExpression(
      page,
      `Boolean(document.querySelector('[aria-label="数据备份与还原"] .backup-table') &&
        Array.from(document.querySelectorAll('[aria-label="数据备份与还原"] button'))
          .some((button) => (button.innerText || '').includes('创建备份') && !button.disabled))`,
      timeoutMs,
      "Timed out waiting for the backup create button.",
    );

    const initialState = await readBackupManagementState(page);
    const initialPathKeys = new Set(initialState.rows.map((row) => normalizePathForCompare(row.fullPath || row.fileName)));
    const createAction = await runBackupManagementUiAction(page, "create");
    let createdRow = null;

    try {
      createdRow = await waitFor(async () => {
        const state = await readBackupManagementState(page);
        const candidate = state.rows.find((row) => {
          const key = normalizePathForCompare(row.fullPath || row.fileName);
          return row.fileName.toLocaleLowerCase().endsWith(".zip") &&
            key &&
            !initialPathKeys.has(key) &&
            row.fullPath &&
            state.backupRoot &&
            isPathInsideRoot(row.fullPath, state.backupRoot) &&
            existsSync(row.fullPath);
        });

        return candidate ? { ...candidate, backupRoot: state.backupRoot, rowCount: state.rows.length } : null;
      }, timeoutMs, () =>
        [
          "Timed out waiting for a newly created backup row.",
          `Initial rows: ${JSON.stringify(initialState.rows)}`,
        ].join("\n"),
      );

      return {
        url: redactDesktopAccessToken(checkUrl),
        createAction,
        initialCount: initialState.rows.length,
        createdBackup: {
          fileName: createdRow.fileName,
          fullPath: createdRow.fullPath,
          sizeText: createdRow.sizeText,
          backupRoot: createdRow.backupRoot,
        },
        createdFileExists: existsSync(createdRow.fullPath),
        cleanedCreatedFile: cleanupSmokeBackupFile(createdRow.fullPath, createdRow.backupRoot),
      };
    } finally {
      if (createdRow?.fullPath && existsSync(createdRow.fullPath)) {
        cleanupSmokeBackupFile(createdRow.fullPath, createdRow.backupRoot);
      }
    }
  }

  async function waitForBackupRestoreCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.backupRestoreCheck) {
      return null;
    }

    const checkUrl = buildBackupCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    await waitForPageExpression(
      page,
      `Boolean(document.querySelector('[aria-label="数据备份与还原"] .backup-table') &&
        Array.from(document.querySelectorAll('[aria-label="数据备份与还原"] button'))
          .some((button) => (button.innerText || '').includes('创建备份') && !button.disabled))`,
      timeoutMs,
      "Timed out waiting for the backup restore smoke panel.",
    );

    const initialState = await readBackupManagementState(page);
    const initialPathKeys = new Set(initialState.rows.map((row) => normalizePathForCompare(row.fullPath || row.fileName)));
    const suffix = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
    const username = `restore-ui-${suffix}`;
    const password = `Restore-${suffix}!`;
    let createdRow = null;
    let transientUser = null;
    let restoreSubmitted = false;

    try {
      await delay(1100);
      const createAction = await runBackupManagementUiAction(page, "create");
      createdRow = await waitFor(async () => {
        const state = await readBackupManagementState(page);
        const candidate = state.rows.find((row) => {
          const key = normalizePathForCompare(row.fullPath || row.fileName);
          return row.fileName.toLocaleLowerCase().endsWith(".zip") &&
            key &&
            !initialPathKeys.has(key) &&
            row.fullPath &&
            state.backupRoot &&
            isPathInsideRoot(row.fullPath, state.backupRoot) &&
            existsSync(row.fullPath);
        });

        return candidate ? { ...candidate, backupRoot: state.backupRoot, rowCount: state.rows.length } : null;
      }, timeoutMs, () =>
        [
          "Timed out waiting for a newly created backup row for restore smoke.",
          `Initial rows: ${JSON.stringify(initialState.rows)}`,
        ].join("\n"),
      );

      transientUser = await createBackupRestoreTransientUser(options, accessToken, tokenType, {
        username,
        password,
        fullName: `Restore UI User ${suffix}`,
      });
      const usersBeforeRestore = await listApiUsers(options, accessToken, tokenType);
      const createdUserBeforeRestore = usersBeforeRestore.users.some((user) => user.username === username);
      if (!createdUserBeforeRestore) {
        throw new Error(`Transient restore user was not visible before restore: ${username}`);
      }

      const restoreAction = await runBackupManagementUiAction(page, "restore", {
        backupFileName: createdRow.fileName,
        confirmationText: "RESTORE",
      });
      restoreSubmitted = true;

      const restoreSuccessMessage = await waitForBackupRestoreSuccessMessage(page, timeoutMs);
      return {
        url: redactDesktopAccessToken(checkUrl),
        initialCount: initialState.rows.length,
        createAction,
        backupFile: {
          fileName: createdRow.fileName,
          fullPath: createdRow.fullPath,
          sizeText: createdRow.sizeText,
          backupRoot: createdRow.backupRoot,
        },
        transientUser: {
          id: transientUser.id,
          username,
        },
        createdUserBeforeRestore,
        restoreAction,
        restoreSuccessMessage,
        restoreRequiresRestart: includesText(restoreSuccessMessage, "重启"),
        backupFileExistsAfterRestore: existsSync(createdRow.fullPath),
      };
    } finally {
      if (!restoreSubmitted && transientUser?.id) {
        await deleteApiUser(options, accessToken, tokenType, transientUser.id).catch(() => undefined);
      }

      if (!restoreSubmitted && createdRow?.fullPath && existsSync(createdRow.fullPath)) {
        cleanupSmokeBackupFile(createdRow.fullPath, createdRow.backupRoot);
      }
    }
  }

  async function waitForBackupRestoreSuccessMessage(page, timeoutMs) {
    return waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="数据备份与还原"]');
          const messages = Array.from(section?.querySelectorAll('.success-alert') || [])
            .map((item) => item.innerText || item.textContent || '')
            .filter(Boolean);
          return messages.find((message) =>
            message.includes('数据库已从备份还原') ||
            (message.includes('数据库已还原') && message.includes('重启'))) || '';
        })()`,
        true,
      );

      const message = String(result.value ?? "");
      return message ? message : null;
    }, timeoutMs, "Timed out waiting for backup restore success message.");
  }

  async function createBackupRestoreTransientUser(options, accessToken, tokenType, user) {
    const response = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        username: user.username,
        fullName: user.fullName,
        role: "User",
        departmentId: "RESTORE-SMOKE",
        companyScope: "SMOKE",
        isActive: true,
        resetPassword: user.password,
      }),
    });

    if (!response.ok) {
      throw new Error(`Backup restore transient user create failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.user?.id) {
      throw new Error(`Backup restore transient user create response did not include user id: ${JSON.stringify(payload)}`);
    }

    return payload.user;
  }

  async function listApiUsers(options, accessToken, tokenType) {
    const response = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
      headers: authorizedHeaders(options, accessToken, tokenType),
    });

    if (!response.ok) {
      throw new Error(`User list read failed with HTTP ${response.status}: ${await response.text()}`);
    }

    return response.json();
  }

  async function deleteApiUser(options, accessToken, tokenType, userId) {
    const response = await fetch(new URL(`/api/users/${encodeURIComponent(String(userId))}`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "DELETE",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });

    if (!response.ok) {
      throw new Error(`Transient user delete failed with HTTP ${response.status}: ${await response.text()}`);
    }

    return response.json();
  }

  async function readBackupManagementState(page) {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="数据备份与还原"]');
        const backupRoot = section?.querySelector('.detail-value-row strong')?.getAttribute('title') ||
          section?.querySelector('.detail-value-row strong')?.textContent ||
          '';
        const rows = Array.from(section?.querySelectorAll('.backup-table tbody tr') || [])
          .map((row) => {
            const cells = row.cells ? Array.from(row.cells) : [];
            const fileName = (cells[0]?.innerText || '').trim();
            const pathSpan = cells[4]?.querySelector('span');
            const fullPath = (pathSpan?.getAttribute('title') || pathSpan?.innerText || '').trim();
            return {
              fileName,
              sizeText: (cells[1]?.innerText || '').trim(),
              createdAtText: (cells[2]?.innerText || '').trim(),
              lastWriteTimeText: (cells[3]?.innerText || '').trim(),
              fullPath
            };
          })
          .filter((row) => row.fileName && row.fileName !== '暂无备份' && row.fileName !== '加载中' && row.fileName !== '无权限');
        return { backupRoot: backupRoot.trim(), rows };
      })()`,
      true,
    );

    const value = result.value ?? {};
    return {
      backupRoot: String(value.backupRoot ?? ""),
      rows: Array.isArray(value.rows) ? value.rows : [],
    };
  }

  async function runBackupManagementUiAction(page, action, payload = {}) {
    const result = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const section = document.querySelector('[aria-label="数据备份与还原"]');
        if (!section) {
          throw new Error("数据备份与还原区域未找到。");
        }

        const notifyReactChange = (control) => {
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === "function") {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };

        const setNativeValue = (control, value) => {
          const prototype = Object.getPrototypeOf(control);
          const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
          if (descriptor && typeof descriptor.set === "function") {
            descriptor.set.call(control, value);
          } else {
            control.value = value;
          }
          control.focus();
          control.dispatchEvent(new Event("input", { bubbles: true }));
          control.dispatchEvent(new Event("change", { bubbles: true }));
          notifyReactChange(control);
        };

        const fieldByLabel = (labelText) => {
          const labels = Array.from(section.querySelectorAll("label"));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll("span")).some((span) => (span.textContent || "").trim() === labelText));
          if (!label) {
            throw new Error("字段未找到: " + labelText);
          }
          const control = label.querySelector("input, select, textarea");
          if (!control) {
            throw new Error("字段没有可编辑控件: " + labelText);
          }
          return control;
        };

        const waitForButton = async (predicate, description) => {
          const deadline = Date.now() + 8000;
          let latestReason = "";
          while (Date.now() < deadline) {
            const button = Array.from(section.querySelectorAll("button")).find(predicate);
            if (button && !button.disabled) {
              return button;
            }

            latestReason = button ? "按钮仍不可用: " + description : "按钮未找到: " + description;
            await delay(100);
          }

          throw new Error(latestReason || "等待按钮超时: " + description);
        };

        if (payload.action === "create") {
          const button = await waitForButton((item) => (item.innerText || item.textContent || "").includes("创建备份"), "创建备份");
          button.click();
          return { action: payload.action, submitted: true };
        }

        if (payload.action === "restore") {
          setNativeValue(fieldByLabel("还原备份"), payload.backupFileName);
          setNativeValue(fieldByLabel("确认文本"), payload.confirmationText);
          const button = await waitForButton((item) => (item.innerText || item.textContent || "").includes("还原数据库"), "还原数据库");
          button.click();
          return { action: payload.action, submitted: true, backupFileName: payload.backupFileName };
        }

        throw new Error("未知备份管理动作: " + payload.action);
      })(${JSON.stringify({ ...payload, action })})`,
      true,
    );

    return result.value ?? { action, submitted: true };
  }

  function cleanupSmokeBackupFile(filePath, backupRoot) {
    if (!filePath || !backupRoot || !isPathInsideRoot(filePath, backupRoot)) {
      throw new Error(`Refusing to remove backup smoke file outside backup root. filePath=${filePath}; backupRoot=${backupRoot}`);
    }

    rmSync(filePath, { force: true });
    return !existsSync(filePath);
  }

  function buildBackupCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeBackup", "1");
    url.hash = "/settings?section=backup";
    return url.toString();
  }

  return {
    runPreparation,
    runRestore: waitForBackupRestoreCheck,
  };
}
