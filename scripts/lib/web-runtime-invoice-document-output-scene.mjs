import { existsSync, readFileSync } from "node:fs";
import net from "node:net";
import path from "node:path";

export function createInvoiceDocumentOutputSmokeScene(runtime) {
  const {
    authorizedHeaders,
    buildBatchExportSettingsDeepLinkUrl,
    buildDocumentEmailSettingsDeepLinkUrl,
    cleanupSmokeDirectory,
    cleanupSmokeFile,
    cloneJson,
    collectFilesByExtension,
    ensureTrailingSlash,
    evaluate,
    getApiSettings,
    includesText,
    isPathInsideRoot,
    readFileSize,
    redactDesktopAccessToken,
    saveApiSettings,
    setRecordValueKeepingExistingCase,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForInvoiceDocumentPackageJobCheck(page, options, accessToken, tokenType, invoice, timeoutMs) {
    const zipMode = await waitForInvoiceDocumentPackageZipJobCheck(
      page,
      options,
      accessToken,
      tokenType,
      invoice,
      timeoutMs,
    );
    const folderMode = await waitForInvoiceDocumentPackageFolderJobCheck(
      page,
      options,
      accessToken,
      tokenType,
      invoice,
      timeoutMs,
    );
  
    return {
      ...zipMode,
      zipMode,
      folderMode,
      allModesSucceeded:
        String(zipMode.status ?? "").toLowerCase() === "succeeded" &&
        String(folderMode.status ?? "").toLowerCase() === "succeeded" &&
        zipMode.zipHeader === "PK" &&
        folderMode.pdfCount > 0 &&
        folderMode.cleanedOutputDirectory === true,
    };
  }
  
  async function waitForInvoiceDocumentPackageZipJobCheck(page, options, accessToken, tokenType, invoice, timeoutMs) {
    const outputPath = path.join(options.userDataDir, `invoice-document-package-${invoice.id}-${Date.now()}.zip`);
    let result = null;
    let cleanedOutputFile = false;
    let latestUiState = null;
    let latestJob = null;
  
    try {
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const outputPath = ${JSON.stringify(outputPath)};
              const panel = document.querySelector('[aria-label="报表预览"]');
              const output = panel ? panel.querySelector('.document-package-output') : null;
              const zipCheck = output ? output.querySelector('.document-package-zip-check input[type="checkbox"]') : null;
              const setNativeValue = (element, value) => {
                const descriptor = Object.getOwnPropertyDescriptor(element.constructor.prototype, 'value') ||
                  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
                if (descriptor?.set) {
                  descriptor.set.call(element, value);
                } else {
                  element.value = value;
                }
                element.dispatchEvent(new Event('input', { bubbles: true }));
              };
  
              if (!panel || !output || !zipCheck) {
                return { ready: false, reason: 'document package output controls are not ready' };
              }
  
              if (!zipCheck.checked) {
                zipCheck.click();
                return { ready: false, reason: 'switched output mode to zip' };
              }
  
              const pathFields = Array.from(output.querySelectorAll('.path-field'));
              const pathField = pathFields.find((field) =>
                ((field.querySelector('.path-field-label') || {}).innerText || '').trim() === '输出 ZIP');
              const input = pathField ? pathField.querySelector('input') : null;
              if (!input || input.disabled) {
                return { ready: false, reason: 'output zip input is not available' };
              }
  
              if (input.value !== outputPath) {
                setNativeValue(input, outputPath);
              }
  
              const buttons = Array.from(output.querySelectorAll('button'));
              const button = buttons.find((element) => (element.innerText || '').includes('生成 ZIP'));
              return {
                ready: Boolean(button && !button.disabled && input.value === outputPath),
                reason: button ? (button.disabled ? 'generate zip button is disabled' : '') : 'generate zip button was not found',
                outputPath: input.value || '',
                buttonText: button ? button.innerText || '' : '',
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));
  
        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing invoice document package ZIP output. Latest UI state: ${JSON.stringify(latestUiState)}`);
  
      await evaluate(
        page,
        `(() => {
          const output = document.querySelector('[aria-label="报表预览"] .document-package-output');
          const buttons = output ? Array.from(output.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('生成 ZIP'));
          if (!button || button.disabled) {
            throw new Error('Generate ZIP button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const panel = document.querySelector('[aria-label="报表预览"]');
              const text = panel ? panel.innerText || '' : '';
              const match = text.match(/已创建单据包 ZIP任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : '',
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: '', textExcerpt: String(error) } }));
  
        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => "Timed out waiting for the invoice document package ZIP job creation message.");
  
      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET invoice document package job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }
  
        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }
  
        if (status === "failed" || status === "canceled") {
          throw new Error(`Invoice document package job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }
  
        return null;
      }, timeoutMs, () =>
        `Timed out waiting for invoice document package job to finish. Latest: ${JSON.stringify(latestJob)}`);
  
      const outputExists = existsSync(outputPath);
      const outputSize = outputExists ? await readFileSize(outputPath) : 0;
      const header = outputExists ? readFileSync(outputPath).subarray(0, 2).toString("ascii") : "";
      if (!outputExists || outputSize <= 0 || header !== "PK") {
        throw new Error(`Invoice document package ZIP was not created correctly. exists=${outputExists}; size=${outputSize}; header=${header}; path=${outputPath}`);
      }
  
      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        status: completed.status,
        outputPath: completed.outputPath || outputPath,
        outputExists,
        outputSize,
        zipHeader: header,
        cleanedOutputFile,
        storagePolicy: "单据包 ZIP smoke 只写运行数据根 smoke profile 下的显式临时 .zip，任务完成后删除；不创建默认导出目录或系统盘落点。",
      };
    } finally {
      if (existsSync(outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }
  
      if (result) {
        result.cleanedOutputFile = cleanedOutputFile;
      }
    }
  
    return result;
  }
  
  async function waitForInvoiceDocumentPackageFolderJobCheck(page, options, accessToken, tokenType, invoice, timeoutMs) {
    const outputRoot = path.join(options.userDataDir, `invoice-document-package-folder-${invoice.id}-${Date.now()}`);
    let result = null;
    let cleanedOutputDirectory = false;
    let latestUiState = null;
    let latestJob = null;
  
    try {
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const outputPath = ${JSON.stringify(outputRoot)};
              const panel = document.querySelector('[aria-label="报表预览"]');
              const output = panel ? panel.querySelector('.document-package-output') : null;
              const zipCheck = output ? output.querySelector('.document-package-zip-check input[type="checkbox"]') : null;
              const setNativeValue = (element, value) => {
                const descriptor = Object.getOwnPropertyDescriptor(element.constructor.prototype, 'value') ||
                  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
                if (descriptor?.set) {
                  descriptor.set.call(element, value);
                } else {
                  element.value = value;
                }
                element.dispatchEvent(new Event('input', { bubbles: true }));
              };
  
              if (!panel || !output || !zipCheck) {
                return { ready: false, reason: 'document package output controls are not ready' };
              }
  
              if (zipCheck.checked) {
                zipCheck.click();
                return { ready: false, reason: 'switched output mode to folder' };
              }
  
              const pathFields = Array.from(output.querySelectorAll('.path-field'));
              const pathField = pathFields.find((field) =>
                ((field.querySelector('.path-field-label') || {}).innerText || '').trim() === '输出文件夹');
              const input = pathField ? pathField.querySelector('input') : null;
              if (!input || input.disabled) {
                return { ready: false, reason: 'output folder input is not available' };
              }
  
              if (input.value !== outputPath) {
                setNativeValue(input, outputPath);
              }
  
              const buttons = Array.from(output.querySelectorAll('button'));
              const button = buttons.find((element) => (element.innerText || '').includes('导出文件夹'));
              return {
                ready: Boolean(button && !button.disabled && input.value === outputPath),
                reason: button ? (button.disabled ? 'export folder button is disabled' : '') : 'export folder button was not found',
                outputPath: input.value || '',
                buttonText: button ? button.innerText || '' : '',
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));
  
        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing invoice document package folder output. Latest UI state: ${JSON.stringify(latestUiState)}`);
  
      await evaluate(
        page,
        `(() => {
          const output = document.querySelector('[aria-label="报表预览"] .document-package-output');
          const buttons = output ? Array.from(output.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('导出文件夹'));
          if (!button || button.disabled) {
            throw new Error('Export folder button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const panel = document.querySelector('[aria-label="报表预览"]');
              const text = panel ? panel.innerText || '' : '';
              const match = text.match(/已创建单据文件夹导出任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : '',
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: '', textExcerpt: String(error) } }));
  
        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => "Timed out waiting for the invoice document package folder job creation message.");
  
      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET invoice document package folder job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }
  
        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }
  
        if (status === "failed" || status === "canceled") {
          throw new Error(`Invoice document package folder job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }
  
        return null;
      }, timeoutMs, () =>
        `Timed out waiting for invoice document package folder job to finish. Latest: ${JSON.stringify(latestJob)}`);
  
      const jobOutputPath = completed.outputPath || "";
      if (!jobOutputPath || !isPathInsideRoot(jobOutputPath, outputRoot)) {
        throw new Error(`Invoice document package folder job returned an output path outside the smoke root. outputPath=${jobOutputPath}; root=${outputRoot}`);
      }
  
      const outputRootExists = existsSync(outputRoot);
      const outputDirectoryExists = existsSync(jobOutputPath);
      const pdfFiles = outputDirectoryExists ? await collectFilesByExtension(jobOutputPath, ".pdf") : [];
      const pdfSizes = await Promise.all(pdfFiles.map((filePath) => readFileSize(filePath)));
      const pdfHeaders = pdfFiles.map((filePath) => readFileSync(filePath).subarray(0, 4).toString("ascii"));
      if (
        !outputRootExists ||
        !outputDirectoryExists ||
        pdfFiles.length === 0 ||
        pdfSizes.some((size) => size <= 0) ||
        pdfHeaders.some((header) => header !== "%PDF")
      ) {
        throw new Error(
          `Invoice document package folder output was not created correctly. rootExists=${outputRootExists}; directoryExists=${outputDirectoryExists}; pdfCount=${pdfFiles.length}; headers=${pdfHeaders.join(",")}; outputPath=${jobOutputPath}`,
        );
      }
  
      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        status: completed.status,
        outputRoot,
        outputPath: jobOutputPath,
        outputRootExists,
        outputDirectoryExists,
        pdfCount: pdfFiles.length,
        pdfFiles: pdfFiles.map((filePath) => path.relative(outputRoot, filePath)),
        pdfSizes,
        pdfHeaders,
        cleanedOutputDirectory,
        storagePolicy: "单据包文件夹 smoke 只写运行数据根 smoke profile 下的显式临时目录，任务完成后删除；不创建默认导出目录或系统盘落点。",
      };
    } finally {
      if (existsSync(outputRoot)) {
        cleanedOutputDirectory = cleanupSmokeDirectory(outputRoot, options.userDataDir);
      } else {
        cleanedOutputDirectory = true;
      }
  
      if (result) {
        result.cleanedOutputDirectory = cleanedOutputDirectory;
      }
    }
  
    return result;
  }
  
  async function waitForInvoiceDocumentEmailJobCheck(page, options, accessToken, tokenType, invoice, timeoutMs) {
    const smtpServer = await startSmokeSmtpServer(timeoutMs);
    const originalSettings = await getApiSettings(options, accessToken, tokenType);
    const originalSettingsBody = cloneJson(originalSettings.settings ?? {});
    const smokeRecipient = `docs-${invoice.id}-${Date.now()}@smoke.local`;
    const smokeSubject = `Smoke document email ${invoice.invoiceNo}`;
    const smokeBody = `Smoke generated export documents for ${invoice.invoiceNo}.`;
    let restoredSettings = false;
    let latestUiState = null;
    let latestJob = null;
    let result = null;
  
    try {
      await saveApiSettings(
        options,
        accessToken,
        tokenType,
        buildSmokeSmtpSettings(originalSettingsBody, smtpServer.port),
      );
  
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const recipient = ${JSON.stringify(smokeRecipient)};
              const subject = ${JSON.stringify(smokeSubject)};
              const body = ${JSON.stringify(smokeBody)};
              const panel = document.querySelector('[aria-label="报表预览"]');
              const emailPanel = panel ? panel.querySelector('.document-email-panel') : null;
              const setNativeValue = (element, value) => {
                const descriptor = Object.getOwnPropertyDescriptor(element.constructor.prototype, 'value') ||
                  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value') ||
                  Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
                if (descriptor?.set) {
                  descriptor.set.call(element, value);
                } else {
                  element.value = value;
                }
                element.dispatchEvent(new Event('input', { bubbles: true }));
              };
  
              if (!emailPanel) {
                return { ready: false, reason: 'document email panel is not ready' };
              }
  
              const recipientInput = emailPanel.querySelector('input[type="email"]');
              const textInputs = Array.from(emailPanel.querySelectorAll('.document-email-grid input'));
              const subjectInput = textInputs.find((input) => input !== recipientInput);
              const bodyInput = emailPanel.querySelector('textarea');
              const mergeCheck = emailPanel.querySelector('.document-email-actions input[type="checkbox"]');
              if (!recipientInput || !subjectInput || !bodyInput || !mergeCheck) {
                return { ready: false, reason: 'document email inputs are not available' };
              }
  
              if (recipientInput.value !== recipient) {
                setNativeValue(recipientInput, recipient);
              }
              if (subjectInput.value !== subject) {
                setNativeValue(subjectInput, subject);
              }
              if (bodyInput.value !== body) {
                setNativeValue(bodyInput, body);
              }
              if (!mergeCheck.disabled && !mergeCheck.checked) {
                mergeCheck.click();
                return { ready: false, reason: 'enabled merged PDF attachment' };
              }
  
              const buttons = Array.from(emailPanel.querySelectorAll('button'));
              const button = buttons.find((element) => (element.innerText || '').includes('发送邮件'));
              return {
                ready: Boolean(button && !button.disabled &&
                  recipientInput.value === recipient &&
                  subjectInput.value === subject &&
                  bodyInput.value === body &&
                  (mergeCheck.disabled || mergeCheck.checked)),
                reason: button ? (button.disabled ? 'send email button is disabled' : '') : 'send email button was not found',
                recipient: recipientInput.value || '',
                subject: subjectInput.value || '',
                bodyLength: bodyInput.value.length,
                includeMergedPdf: mergeCheck.checked,
                buttonText: button ? button.innerText || '' : '',
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));
  
        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing invoice document email send. Latest UI state: ${JSON.stringify(latestUiState)}`);
  
      await evaluate(
        page,
        `(() => {
          const emailPanel = document.querySelector('[aria-label="报表预览"] .document-email-panel');
          const buttons = emailPanel ? Array.from(emailPanel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('发送邮件'));
          if (!button || button.disabled) {
            throw new Error('Send document email button is not available.');
          }
  
          button.click();
          return true;
        })()`,
        true,
      );
  
      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const panel = document.querySelector('[aria-label="报表预览"]');
              const text = panel ? panel.innerText || '' : '';
              const match = text.match(/已创建单据邮件任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : '',
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: '', textExcerpt: String(error) } }));
  
        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => "Timed out waiting for the invoice document email job creation message.");
  
      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET invoice document email job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }
  
        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }
  
        if (status === "failed" || status === "canceled") {
          throw new Error(`Invoice document email job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }
  
        return null;
      }, timeoutMs, () =>
        `Timed out waiting for invoice document email job to finish. Latest: ${JSON.stringify(latestJob)}`);
  
      const message = await smtpServer.waitForMessage(timeoutMs);
      const rawMessage = message.raw || "";
      const attachmentNameMatches = rawMessage.match(/(?:filename|name)\*?="?[^"\r\n;]*(?:\.pdf|%2Epdf)[^"\r\n;]*"?/gi) ?? [];
      const attachmentDispositionCount = (rawMessage.match(/Content-Disposition:\s*attachment/gi) ?? []).length;
      const pdfMimeCount = (rawMessage.match(/Content-Type:\s*application\/pdf/gi) ?? []).length;
      const pdfBase64HeaderCount = (rawMessage.match(/JVBERi0/g) ?? []).length;
      const pdfAttachmentCount = Math.max(
        attachmentNameMatches.length,
        attachmentDispositionCount,
        pdfMimeCount,
        pdfBase64HeaderCount,
      );
      const cacheRoot = path.join(path.dirname(options.userDataDir), "Cache", "ReportDocumentEmails", completed.jobId);
      const tempCacheCleaned = !existsSync(cacheRoot);
      if (
        !rawMessage.includes(smokeRecipient) ||
        !rawMessage.includes(smokeSubject) ||
        !rawMessage.includes(invoice.invoiceNo) ||
        pdfAttachmentCount === 0 ||
        !tempCacheCleaned
      ) {
        throw new Error(
          [
            "Invoice document email smoke did not receive the expected generated message.",
            `recipientFound=${rawMessage.includes(smokeRecipient)}`,
            `subjectFound=${rawMessage.includes(smokeSubject)}`,
            `invoiceFound=${rawMessage.includes(invoice.invoiceNo)}`,
            `pdfAttachmentCount=${pdfAttachmentCount}`,
            `tempCacheCleaned=${tempCacheCleaned}`,
          ].join(" "),
        );
      }
  
      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        status: completed.status,
        recipient: smokeRecipient,
        subject: smokeSubject,
        smtpPort: smtpServer.port,
        smtpMessageCount: smtpServer.messages.length,
        rawMessageLength: rawMessage.length,
        pdfAttachmentCount,
        attachmentNameCount: attachmentNameMatches.length,
        attachmentDispositionCount,
        pdfMimeCount,
        pdfBase64HeaderCount,
        tempCacheCleaned,
        storagePolicy: "单据邮件 smoke 只启动本机回环 SMTP 服务并临时写程序根设置；附件临时 PDF 位于运行数据根 Cache/ReportDocumentEmails/{jobId}，任务完成后清理，不创建默认附件目录或系统盘落点。",
      };
    } finally {
      await saveApiSettings(options, accessToken, tokenType, originalSettingsBody)
        .then(() => {
          restoredSettings = true;
        })
        .catch((error) => {
          if (!result) {
            throw error;
          }
          result.restoreSettingsError = error.message;
        });
      await smtpServer.close();
      if (result) {
        result.restoredSettings = restoredSettings;
        result.smtpClosed = true;
      }
    }
  
    return result;
  }
  
  async function waitForBatchExportSettingsDeepLinkCheck(page, options, timeoutMs) {
    const checkUrl = buildBatchExportSettingsDeepLinkUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "设置",
      "模板设置",
      "单证模板设置",
      "文件命名规则",
      "导出项",
      "新增单证",
    ];
    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const panelCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单证模板设置"]');
        if (!panel || !window.location.hash.includes('/settings?section=batchExport')) {
          return false;
        }
  
        const rect = panel.getBoundingClientRect();
        return rect.bottom > 0 && rect.top < Math.max(120, window.innerHeight * 0.75);
      })()`,
      timeoutMs,
      "Timed out waiting for the batch export settings deep link to focus the panel.",
    );
  
    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      panelCheck,
    };
  }
  
  async function waitForDocumentEmailSettingsDeepLinkCheck(page, options, timeoutMs) {
    const checkUrl = buildDocumentEmailSettingsDeepLinkUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "设置",
      "邮件与备份",
      "SMTP 服务器",
      "单据邮件主题",
      "单据邮件正文",
    ];
    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const panelCheck = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="邮件与备份"]');
        if (!panel || !window.location.hash.includes('/settings?section=email')) {
          return false;
        }
  
        const rect = panel.getBoundingClientRect();
        return rect.bottom > 0 && rect.top < Math.max(120, window.innerHeight * 0.75);
      })()`,
      timeoutMs,
      "Timed out waiting for the document email settings deep link to focus the panel.",
    );
  
    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      panelCheck,
    };
  }

  function buildSmokeSmtpSettings(settings, smtpPort) {
    const smokeSettings = cloneJson(settings ?? {});
    const currentEmail = smokeSettings.email ?? smokeSettings.Email ?? {};
    const emailSettings = { ...currentEmail };
    setRecordValueKeepingExistingCase(emailSettings, ["smtpHost", "SmtpHost"], "127.0.0.1");
    setRecordValueKeepingExistingCase(emailSettings, ["smtpPort", "SmtpPort"], smtpPort);
    setRecordValueKeepingExistingCase(emailSettings, ["enableSsl", "EnableSsl"], false);
    setRecordValueKeepingExistingCase(emailSettings, ["userName", "UserName"], "");
    setRecordValueKeepingExistingCase(emailSettings, ["fromAddress", "FromAddress"], "exportdoc-smoke@example.test");
    setRecordValueKeepingExistingCase(emailSettings, ["fromDisplayName", "FromDisplayName"], "ExportDoc Smoke");
    setRecordValueKeepingExistingCase(
      emailSettings,
      ["documentEmailSubjectTemplate", "DocumentEmailSubjectTemplate"],
      "Export Documents for Invoice {InvoiceNo}",
    );
    setRecordValueKeepingExistingCase(
      emailSettings,
      ["documentEmailBodyTemplate", "DocumentEmailBodyTemplate"],
      "Dear Customer,\\n\\nPlease find the attached export documents for {InvoiceNo}.",
    );
    setRecordValueKeepingExistingCase(smokeSettings, ["email", "Email"], emailSettings);
    return smokeSettings;
  }

  async function startSmokeSmtpServer(timeoutMs) {
    const messages = [];
    const sockets = new Set();
    const server = net.createServer((socket) => {
      sockets.add(socket);
      socket.setEncoding("utf8");
      socket.write("220 exportdoc-smoke-smtp\r\n");
  
      let buffer = "";
      let dataMode = false;
      let dataLines = [];
      let envelope = { mailFrom: "", rcptTo: [] };
  
      socket.on("data", (chunk) => {
        buffer += chunk;
        while (true) {
          const index = buffer.indexOf("\n");
          if (index < 0) {
            break;
          }
  
          let line = buffer.slice(0, index);
          buffer = buffer.slice(index + 1);
          if (line.endsWith("\r")) {
            line = line.slice(0, -1);
          }
  
          if (dataMode) {
            if (line === ".") {
              messages.push({
                mailFrom: envelope.mailFrom,
                rcptTo: [...envelope.rcptTo],
                raw: dataLines.join("\r\n"),
              });
              dataMode = false;
              dataLines = [];
              socket.write("250 2.0.0 OK\r\n");
            } else {
              dataLines.push(line.startsWith("..") ? line.slice(1) : line);
            }
            continue;
          }
  
          const upper = line.toUpperCase();
          if (upper.startsWith("EHLO") || upper.startsWith("HELO")) {
            socket.write("250-localhost\r\n250 OK\r\n");
          } else if (upper.startsWith("MAIL FROM:")) {
            envelope = { mailFrom: line.slice("MAIL FROM:".length).trim(), rcptTo: [] };
            socket.write("250 2.1.0 OK\r\n");
          } else if (upper.startsWith("RCPT TO:")) {
            envelope.rcptTo.push(line.slice("RCPT TO:".length).trim());
            socket.write("250 2.1.5 OK\r\n");
          } else if (upper === "DATA") {
            dataMode = true;
            dataLines = [];
            socket.write("354 End data with <CR><LF>.<CR><LF>\r\n");
          } else if (upper === "RSET") {
            envelope = { mailFrom: "", rcptTo: [] };
            dataMode = false;
            dataLines = [];
            socket.write("250 2.0.0 OK\r\n");
          } else if (upper === "NOOP") {
            socket.write("250 2.0.0 OK\r\n");
          } else if (upper === "QUIT") {
            socket.write("221 2.0.0 Bye\r\n");
            socket.end();
          } else {
            socket.write("250 2.0.0 OK\r\n");
          }
        }
      });
  
      socket.on("close", () => sockets.delete(socket));
      socket.on("error", () => sockets.delete(socket));
    });
  
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        server.close();
        reject(new Error("Timed out starting smoke SMTP server."));
      }, Math.min(timeoutMs, 10000));
      server.once("error", (error) => {
        clearTimeout(timer);
        reject(error);
      });
      server.listen(0, "127.0.0.1", () => {
        clearTimeout(timer);
        resolve();
      });
    });
  
    return {
      port: server.address().port,
      messages,
      waitForMessage: (messageTimeoutMs) =>
        waitFor(
          () => messages[0] ?? null,
          messageTimeoutMs,
          () => `Timed out waiting for local SMTP message. Received ${messages.length}.`,
        ),
      close: () =>
        new Promise((resolve) => {
          for (const socket of sockets) {
            socket.destroy();
          }
          if (!server.listening) {
            resolve();
            return;
          }
  
          server.close(() => resolve());
        }),
    };
  }

  return {
    runDocumentEmailJob: waitForInvoiceDocumentEmailJobCheck,
    runDocumentEmailSettingsDeepLink: waitForDocumentEmailSettingsDeepLinkCheck,
    runPackageJobs: waitForInvoiceDocumentPackageJobCheck,
    runPackageSettingsDeepLink: waitForBatchExportSettingsDeepLinkCheck,
  };
}
