import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

const SESSION_STORAGE_KEY = "exportdocmanager.web.session";

export function createInvoiceListDesktopSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    buildSmokeAgentConsignmentReceiptXml,
    buildSmokeCustomsCooReceiptXml,
    createSmokeInvoice,
    deleteSmokeInvoice,
    desktopAccessHeaders,
    ensureTrailingSlash,
    evaluate,
    getSingleWindowBatchDetail,
    normalizePathForCompare,
    readFileSize,
    redactDesktopAccessToken,
    tryRemoveDirectory,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
    waitForTauriCommandInvocation,
  } = runtime;

  function buildInvoiceListDesktopWorkflowCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeInvoiceListDesktopWorkflow", "1");
    url.hash = "/invoices";
    return url.toString();
  }
  
  async function waitForInvoiceListDesktopWorkflowCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.invoiceListDesktopWorkflowCheck) {
      return null;
    }
  
    const timestamp = Date.now();
    const smokeRoot = path.join(options.userDataDir, `invoice-list-desktop-workflow-${timestamp}`);
    const transferPackagePath = path.join(smokeRoot, `invoice-transfer-${timestamp}.edpkg`);
    const bookingSheetPath = path.join(smokeRoot, `invoice-booking-${timestamp}.xlsx`);
    const cooSubmitPackagePath = path.join(smokeRoot, `invoice-list-coo-${timestamp}.swpkg`);
    const acdSubmitPackagePath = path.join(smokeRoot, `invoice-list-acd-${timestamp}.swpkg`);
    const cooReceiptPackagePath = path.join(smokeRoot, `coo-receipt-${timestamp}.swpkg`);
    const acdReceiptPackagePath = path.join(smokeRoot, `acd-receipt-${timestamp}.swpkg`);
    const stationRoot = path.join(smokeRoot, "OfficialClient");
    const cooClientRoot = path.join(stationRoot, "COO");
    const acdClientRoot = path.join(stationRoot, "ACD");
    const cooReceiptInbox = path.join(cooClientRoot, "InBox");
    const acdReceiptInbox = path.join(acdClientRoot, "InBox");
    const url = buildInvoiceListDesktopWorkflowCheckUrl(options.webUrl);
    let invoice = null;
    let operatorUserId = 0;
    let operatorDeleted = false;
    let browserSessionOverrideId = "";
    let browserSessionSwitched = false;
    let result = null;
    let deletedInvoice = false;
    let cleanedSmokeRoot = false;
  
    try {
      mkdirSync(smokeRoot, { recursive: true });
      const companyScope = `Smoke Company Invoice List ${timestamp}`;
      const operator = await createScopedSmokeUser(
        options,
        accessToken,
        tokenType,
        companyScope,
        timestamp,
      );
      operatorUserId = operator.userId;
      const operatorLogin = await loginScopedSmokeUser(options, operator.username, operator.password);
      invoice = await createSmokeInvoice(options, operatorLogin.accessToken, operatorLogin.tokenType);
      const stationProfile = await saveStationProfile(
        options,
        operatorLogin.accessToken,
        operatorLogin.tokenType,
        companyScope,
        cooClientRoot,
        acdClientRoot,
        timestamp,
      );
      const stationAssignmentCode = stationProfile.stationAssignmentCode;
      if (typeof stationAssignmentCode !== "string" || !stationAssignmentCode.startsWith("SWAC1.")) {
        throw new Error("Single Window invoice-list station profile did not return a valid assignment code.");
      }
      browserSessionOverrideId = await installBrowserSessionOverride(page, {
        accessToken: operatorLogin.accessToken,
        expiresAt: operatorLogin.expiresAt,
        apiBaseUrl: options.apiBaseUrl,
        user: operatorLogin.user,
      });
      browserSessionSwitched = true;
      await navigateToInvoiceListSmokeRow(page, options, invoice, timeoutMs);
      await waitForPageExpression(
        page,
        `document.body && (document.body.innerText || '').includes(${JSON.stringify("Single Window Invoice List Smoke")})`,
        timeoutMs,
        "Timed out waiting for the scoped invoice-list browser session.",
      );
  
      const transferExport = await runInvoiceListTransferExportCheck(
        page,
        invoice,
        transferPackagePath,
        timeoutMs,
      );
      const transferImport = await runInvoiceListTransferImportCheck(
        page,
        options,
        invoice,
        transferPackagePath,
        timeoutMs,
      );
      const bookingSheet = await runInvoiceListBookingSheetExportCheck(
        page,
        options,
        operatorLogin.accessToken,
        operatorLogin.tokenType,
        invoice,
        bookingSheetPath,
        timeoutMs,
      );
      const singleWindow = await runInvoiceListSingleWindowCheck(
        page,
        options,
        operatorLogin.accessToken,
        operatorLogin.tokenType,
        invoice,
        stationAssignmentCode,
        {
          cooSubmitPackagePath,
          acdSubmitPackagePath,
          cooReceiptPackagePath,
          acdReceiptPackagePath,
          cooClientRoot,
          acdClientRoot,
          cooReceiptInbox,
          acdReceiptInbox,
        },
        timeoutMs,
      );
  
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        url: redactDesktopAccessToken(url),
        transferExport,
        transferImport,
        bookingSheet,
        singleWindow,
        allSucceeded: Boolean(
          transferExport?.packageHeader === "PK" &&
            transferExport?.saveDialogInvocation === true &&
            transferImport?.openDialogInvocation === true &&
            transferImport?.submitted === true &&
            bookingSheet?.fileHeader === "PK" &&
            bookingSheet?.saveDialogInvocation === true &&
            singleWindow?.coo?.submitPackageHeader === "PK" &&
            singleWindow?.acd?.submitPackageHeader === "PK" &&
            singleWindow?.coo?.stationImported === true &&
            singleWindow?.acd?.stationImported === true &&
            singleWindow?.coo?.stationDispatched === true &&
            singleWindow?.acd?.stationDispatched === true &&
            singleWindow?.coo?.stationReceiptCollected === true &&
            singleWindow?.acd?.stationReceiptCollected === true &&
            singleWindow?.coo?.detailStatus === "Approved" &&
            singleWindow?.acd?.detailStatus === "Accepted" &&
            singleWindow?.coo?.receiptImportDialogInvocation === true &&
            singleWindow?.acd?.receiptImportDialogInvocation === true),
        deletedInvoice,
        deletedOperator: operatorDeleted,
        cleanedSmokeRoot,
        companyScope,
        stationProfileKey: stationProfile.profileKey,
        dataBoundary: "发票列表桌面闭环 smoke 由 invoice.id 触发托单、发票单据包和单一窗口 COO/ACD 提交包；发票号只用于列表检索和回执批次引用，不作为付款/报销或实际/报关数据合并键。",
        storagePolicy: "所有列表级桌面文件选择输出均由 Tauri mock 文件对话框显式返回，并写入 smoke userDataDir 下的临时运行目录；完成后删除临时输出目录。",
      };
    } finally {
      if (browserSessionSwitched) {
        await restoreBrowserSession(page, browserSessionOverrideId, options.webUrl, timeoutMs)
          .catch(() => undefined);
      }

      if (invoice?.id) {
        deletedInvoice = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      } else {
        deletedInvoice = true;
      }

      if (operatorUserId) {
        operatorDeleted = await deleteScopedSmokeUser(
          options,
          accessToken,
          tokenType,
          operatorUserId,
        ).catch(() => false);
      } else {
        operatorDeleted = true;
      }
  
      if (existsSync(smokeRoot)) {
        cleanedSmokeRoot = tryRemoveDirectory(smokeRoot);
      } else {
        cleanedSmokeRoot = true;
      }
  
      if (result) {
        result.deletedInvoice = deletedInvoice;
        result.deletedOperator = operatorDeleted;
        result.cleanedSmokeRoot = cleanedSmokeRoot;
      }
    }
  
    return result;
  }
  
  async function navigateToInvoiceListSmokeRow(page, options, invoice, timeoutMs) {
    const checkUrl = buildInvoiceListDesktopWorkflowCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    await waitForRuntimeDiagnostics(page, ["发票号", "导入单据包", "新建"], timeoutMs);
  
    await evaluate(
      page,
      `(() => {
        const input = document.querySelector('input[aria-label="搜索发票"]');
        if (!input) {
          throw new Error('Invoice list search input was not found.');
        }
  
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(input, ${JSON.stringify(invoice.invoiceNo)});
        input.dispatchEvent(new Event('input', { bubbles: true }));
        const form = input.closest('form');
        if (form) {
          form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        }
        return true;
      })()`,
      true,
    );
  
    return waitForPageExpression(
      page,
      `(() => {
        const rows = Array.from(document.querySelectorAll('tbody tr'));
        return rows.some((row) => (row.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}));
      })()`,
      timeoutMs,
      `Timed out waiting for invoice list row: ${invoice.invoiceNo}`,
    );
  }
  
  async function runInvoiceListTransferExportCheck(page, invoice, packagePath, timeoutMs) {
    await waitForPageExpression(
      page,
      `(() => {
        const button = document.querySelector(${JSON.stringify(`button[aria-label="导出单据包 ${invoice.invoiceNo}"]`)});
        return Boolean(button && !button.disabled);
      })()`,
      timeoutMs,
      "Timed out waiting for the invoice transfer export button to become available.",
    );
  
    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeSaveInvoiceTransferPackagePath = ${JSON.stringify(packagePath)};
        window.__exportDocManagerSmokeTauriInvocations = [];
        const button = document.querySelector(${JSON.stringify(`button[aria-label="导出单据包 ${invoice.invoiceNo}"]`)});
        if (!button || button.disabled) {
          throw new Error('Invoice transfer export button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const invocation = await waitForTauriCommandInvocation(
      page,
      "select_save_invoice_transfer_package_path",
      packagePath,
      timeoutMs,
      "Timed out waiting for invoice transfer save dialog invocation.",
    );
    const packageFile = await waitForPackageFile(packagePath, timeoutMs, "invoice transfer package");
    const successUi = await waitForPageExpression(
      page,
      `(() => {
        const text = document.body ? document.body.innerText || '' : '';
        return text.includes('单据包') && text.includes(${JSON.stringify(packagePath)});
      })()`,
      timeoutMs,
      "Timed out waiting for invoice transfer export success message.",
    );
  
    return {
      packagePath,
      packageHeader: packageFile.header,
      packageSize: packageFile.size,
      saveDialogInvocation: Boolean(invocation?.found),
      successUi: Boolean(successUi?.found),
    };
  }
  
  async function runInvoiceListTransferImportCheck(page, options, invoice, packagePath, timeoutMs) {
    await navigateToInvoiceListSmokeRow(page, options, invoice, timeoutMs);
    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeInvoiceTransferPackagePath = ${JSON.stringify(packagePath)};
        window.__exportDocManagerSmokeTauriInvocations = [];
        const button = Array.from(document.querySelectorAll('button'))
          .find((element) => (element.innerText || '').includes('导入单据包'));
        if (!button || button.disabled) {
          throw new Error('Invoice transfer import picker button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const invocation = await waitForTauriCommandInvocation(
      page,
      "select_invoice_transfer_package_file",
      packagePath,
      timeoutMs,
      "Timed out waiting for invoice transfer open dialog invocation.",
    );
    const previewPanel = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="导入发票单据包"]');
        const text = panel ? panel.innerText || '' : '';
        const inputValues = panel ? Array.from(panel.querySelectorAll('input')).map((input) => input.value || '') : [];
        return Boolean(panel &&
          inputValues.includes(${JSON.stringify(invoice.invoiceNo)}) &&
          text.includes('校验通过') &&
          text.includes('同号同类型'));
      })()`,
      timeoutMs,
      "Timed out waiting for invoice transfer import preview panel.",
    );
  
    await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="导入发票单据包"]');
        if (!panel) {
          throw new Error('Invoice transfer import panel was not found before submit.');
        }
  
        const select = panel.querySelector('select');
        if (select) {
          const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set;
          setter.call(select, 'Skip');
          select.dispatchEvent(new Event('input', { bubbles: true }));
          select.dispatchEvent(new Event('change', { bubbles: true }));
        }
  
        const button = Array.from(panel.querySelectorAll('button'))
          .find((element) => (element.innerText || '').trim() === '导入');
        if (!button || button.disabled) {
          throw new Error('Invoice transfer import submit button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const submitted = await waitForPageExpression(
      page,
      `(() => {
        const text = document.body ? document.body.innerText || '' : '';
        return text.includes('单据包已导入') || text.includes('导入完成') || text.includes('导入成功') || text.includes('已跳过');
      })()`,
      timeoutMs,
      "Timed out waiting for invoice transfer import submit result.",
    );
  
    return {
      packagePath,
      openDialogInvocation: Boolean(invocation?.found),
      previewPanel: Boolean(previewPanel?.found),
      submitted: Boolean(submitted?.found),
    };
  }
  
  async function runInvoiceListBookingSheetExportCheck(page, options, accessToken, tokenType, invoice, outputPath, timeoutMs) {
    await navigateToInvoiceListSmokeRow(page, options, invoice, timeoutMs);
    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeSaveExcelPath = ${JSON.stringify(outputPath)};
        window.__exportDocManagerSmokeTauriInvocations = [];
        const button = document.querySelector(${JSON.stringify(`button[aria-label="导出货代订舱托单 ${invoice.invoiceNo}"]`)});
        if (!button || button.disabled) {
          throw new Error('Invoice list booking sheet export button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const invocation = await waitForTauriCommandInvocation(
      page,
      "select_save_excel_path",
      outputPath,
      timeoutMs,
      "Timed out waiting for invoice booking sheet save dialog invocation.",
    );
    const createdState = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const text = document.body ? document.body.innerText || '' : '';
          const match = text.match(/已创建托单导出任务：([^\\s\\n]+)/);
          return {
            jobId: match ? match[1] : '',
            textExcerpt: text.slice(0, 1200),
          };
        })()`,
        true,
      ).catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));
  
      return state.value?.jobId ? state.value : null;
    }, timeoutMs, "Timed out waiting for invoice list booking sheet job creation message.");
  
    const completed = await waitForJobSucceeded(options, accessToken, tokenType, createdState.jobId, timeoutMs, "invoice list booking sheet");
    const outputFile = await waitForPackageFile(outputPath, timeoutMs, "invoice list booking sheet");
  
    return {
      outputPath,
      saveDialogInvocation: Boolean(invocation?.found),
      jobId: completed.jobId,
      kind: completed.kind,
      status: completed.status,
      outputExists: existsSync(outputPath),
      outputSize: outputFile.size,
      fileHeader: outputFile.header,
    };
  }
  
  async function runInvoiceListSingleWindowCheck(
    page,
    options,
    accessToken,
    tokenType,
    invoice,
    stationAssignmentCode,
    paths,
    timeoutMs,
  ) {
    await navigateToInvoiceListSmokeRow(page, options, invoice, timeoutMs);
    await evaluate(
      page,
      `(() => {
        const button = document.querySelector(${JSON.stringify(`button[aria-label="单一窗口办理 ${invoice.invoiceNo}"]`)});
        if (!button || button.disabled) {
          throw new Error('Invoice list Single Window button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const panelReady = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const text = panel ? panel.innerText || '' : '';
        return Boolean(panel &&
          text.includes(${JSON.stringify(invoice.invoiceNo)}) &&
          text.includes('导出 COO 包') &&
          text.includes('导出 ACD 包') &&
          text.includes('导入回执包') &&
          text.includes('导出托单'));
      })()`,
      timeoutMs,
      "Timed out waiting for invoice list Single Window action panel.",
    );

    await setInvoiceListStationAssignmentCode(page, stationAssignmentCode, timeoutMs);
  
    const coo = await runInvoiceListSingleWindowSubmitPackageCheck(
      page,
      options,
      accessToken,
      tokenType,
      invoice,
      "CustomsCoo",
      "导出 COO 包",
      paths.cooSubmitPackagePath,
      timeoutMs,
    );
    const acd = await runInvoiceListSingleWindowSubmitPackageCheck(
      page,
      options,
      accessToken,
      tokenType,
      invoice,
      "AgentConsignment",
      "导出 ACD 包",
      paths.acdSubmitPackagePath,
      timeoutMs,
    );

    const cooStation = await importAndDispatchSubmitPackage(
      options,
      accessToken,
      tokenType,
      coo.batchId,
      paths.cooSubmitPackagePath,
      paths.cooClientRoot,
    );
    const acdStation = await importAndDispatchSubmitPackage(
      options,
      accessToken,
      tokenType,
      acd.batchId,
      paths.acdSubmitPackagePath,
      paths.acdClientRoot,
    );
  
    mkdirSync(paths.cooReceiptInbox, { recursive: true });
    mkdirSync(paths.acdReceiptInbox, { recursive: true });
    const cooStationReceiptPath = path.join(
      paths.cooReceiptInbox,
      `Successed_${coo.batchReference}_${invoice.invoiceNo}.xml`,
    );
    const acdStationReceiptPath = path.join(
      paths.acdReceiptInbox,
      `Successed_${acd.batchReference}_${invoice.invoiceNo}.xml`,
    );
    writeFileSync(cooStationReceiptPath, buildSmokeCustomsCooReceiptXml(coo.batchReference), "utf8");
    writeFileSync(acdStationReceiptPath, buildSmokeAgentConsignmentReceiptXml(acd.batchReference), "utf8");

    const cooReceiptCollection = await collectStationReceipts(
      options,
      accessToken,
      tokenType,
      coo.batchId,
      cooStationReceiptPath,
    );
    const acdReceiptCollection = await collectStationReceipts(
      options,
      accessToken,
      tokenType,
      acd.batchId,
      acdStationReceiptPath,
    );
  
    await exportSmokeSingleWindowReceiptPackage(
      options,
      accessToken,
      tokenType,
      "CustomsCoo",
      invoice.invoiceNo,
      coo.batchReference,
      paths.cooReceiptPackagePath,
      cooReceiptCollection.receiptFiles,
    );
    await exportSmokeSingleWindowReceiptPackage(
      options,
      accessToken,
      tokenType,
      "AgentConsignment",
      invoice.invoiceNo,
      acd.batchReference,
      paths.acdReceiptPackagePath,
      acdReceiptCollection.receiptFiles,
    );
  
    const cooReceiptImport = await runInvoiceListSingleWindowReceiptImportCheck(
      page,
      options,
      accessToken,
      tokenType,
      coo.batchId,
      paths.cooReceiptPackagePath,
      "Smoke approved",
      "Approved",
      timeoutMs,
    );
    const acdReceiptImport = await runInvoiceListSingleWindowReceiptImportCheck(
      page,
      options,
      accessToken,
      tokenType,
      acd.batchId,
      paths.acdReceiptPackagePath,
      "Smoke ACD accepted",
      "Accepted",
      timeoutMs,
    );
  
    return {
      panelReady: Boolean(panelReady?.found),
      coo: { ...coo, ...cooStation, ...cooReceiptCollection, ...cooReceiptImport },
      acd: { ...acd, ...acdStation, ...acdReceiptCollection, ...acdReceiptImport },
    };
  }

  async function importAndDispatchSubmitPackage(
    options,
    accessToken,
    tokenType,
    batchId,
    packagePath,
    expectedClientRoot,
  ) {
    const importResponse = await fetch(new URL("/api/single-window/packages/import", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ packagePath, workingDirectory: "", keepWorkingDirectory: false }),
    });
    if (!importResponse.ok) {
      throw new Error(`Single Window submit package station import failed with HTTP ${importResponse.status}: ${await importResponse.text()}`);
    }
    const imported = await importResponse.json();
    if (!imported?.success || Number(imported.trackingBatchId || 0) !== Number(batchId)) {
      throw new Error(`Single Window submit package station import response is incomplete: ${JSON.stringify(imported)}`);
    }

    const dispatchResponse = await fetch(new URL("/api/single-window/client/dispatch", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ batchId }),
    });
    if (!dispatchResponse.ok) {
      throw new Error(`Single Window station dispatch failed with HTTP ${dispatchResponse.status}: ${await dispatchResponse.text()}`);
    }
    const dispatched = await dispatchResponse.json();
    const expectedOutBox = normalizePathForCompare(path.join(expectedClientRoot, "OutBox"));
    if (Number(dispatched?.batchId || 0) !== Number(batchId) ||
        normalizePathForCompare(dispatched?.targetDirectory) !== expectedOutBox) {
      throw new Error(`Single Window station dispatch response is incomplete: ${JSON.stringify(dispatched)}`);
    }

    return {
      stationImported: true,
      stationDispatched: true,
      stationOutBoxPath: dispatched.targetDirectory,
      stationPayloadFileCount: Number(dispatched.payloadFileCount || 0),
      stationAttachmentFileCount: Number(dispatched.attachmentFileCount || 0),
    };
  }

  async function collectStationReceipts(
    options,
    accessToken,
    tokenType,
    batchId,
    expectedReceiptPath,
  ) {
    const response = await fetch(new URL("/api/single-window/client/collect-receipts", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ batchId }),
    });
    if (!response.ok) {
      throw new Error(`Single Window station receipt collection failed with HTTP ${response.status}: ${await response.text()}`);
    }
    const payload = await response.json();
    const receiptFiles = Array.isArray(payload?.receiptFiles) ? payload.receiptFiles : [];
    if (Number(payload?.batchId || 0) !== Number(batchId) ||
        !receiptFiles.some((candidate) => normalizePathForCompare(candidate) === normalizePathForCompare(expectedReceiptPath))) {
      throw new Error(`Single Window station receipt collection response is incomplete: ${JSON.stringify(payload)}`);
    }
    return {
      stationReceiptCollected: true,
      receiptFiles,
      stationReceiptRootPath: payload.receiptRootPath,
    };
  }

  async function createScopedSmokeUser(options, accessToken, tokenType, companyScope, timestamp) {
    const username = `sw-invoice-list-${timestamp}`;
    const password = `Sw-Invoice-List-${timestamp}!`;
    const fullName = "Single Window Invoice List Smoke";
    const response = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        username,
        fullName,
        role: "Admin",
        departmentId: "SW-SMOKE",
        companyScope,
        isActive: true,
        resetPassword: password,
      }),
    });
    if (!response.ok) {
      throw new Error(`Single Window invoice-list scoped user create failed with HTTP ${response.status}: ${await response.text()}`);
    }
    const payload = await response.json();
    const userId = Number(payload?.user?.id || 0);
    if (!userId) {
      throw new Error(`Single Window invoice-list scoped user response is incomplete: ${JSON.stringify(payload)}`);
    }
    return { userId, username, password, fullName };
  }

  async function loginScopedSmokeUser(options, username, password) {
    const response = await fetch(new URL("/api/auth/login", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: { "Content-Type": "application/json", ...desktopAccessHeaders(options) },
      body: JSON.stringify({ username, password }),
    });
    if (!response.ok) {
      throw new Error(`Single Window invoice-list scoped login failed with HTTP ${response.status}: ${await response.text()}`);
    }
    const payload = await response.json();
    if (!payload?.accessToken || !payload?.expiresAt || !payload?.user) {
      throw new Error(`Single Window invoice-list scoped login response is incomplete: ${JSON.stringify(payload)}`);
    }
    return {
      accessToken: payload.accessToken,
      tokenType: payload.tokenType || "Bearer",
      expiresAt: payload.expiresAt,
      user: payload.user,
    };
  }

  async function installBrowserSessionOverride(page, session) {
    const source = `try {
      window.sessionStorage.setItem(
        ${JSON.stringify(SESSION_STORAGE_KEY)},
        ${JSON.stringify(JSON.stringify(session))}
      );
    } catch (error) {
      console.error('ExportDocManager invoice-list smoke failed to inject scoped session', error);
    }`;
    const result = await page.send("Page.addScriptToEvaluateOnNewDocument", { source });
    if (!result?.identifier) {
      throw new Error("Chrome did not return an identifier for the scoped browser session override.");
    }
    return result.identifier;
  }

  async function restoreBrowserSession(page, overrideId, webUrl, timeoutMs) {
    if (overrideId) {
      await page.send("Page.removeScriptToEvaluateOnNewDocument", { identifier: overrideId });
    }
    await page.send("Page.navigate", { url: webUrl });
    await waitForRuntimeDiagnostics(page, ["工作台"], timeoutMs);
  }

  async function deleteScopedSmokeUser(options, accessToken, tokenType, userId) {
    const response = await fetch(new URL(`/api/users/${encodeURIComponent(String(userId))}`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "DELETE",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });
    return response.ok || response.status === 404;
  }

  async function saveStationProfile(
    options,
    accessToken,
    tokenType,
    companyScope,
    cooClientRoot,
    acdClientRoot,
    timestamp,
  ) {
    const response = await fetch(new URL("/api/single-window/client-profiles", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "PUT",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        profileKey: "",
        profileName: `Smoke Invoice List ${timestamp}`,
        companyScope,
        cardIdentifier: `SMOKE-INVOICE-LIST-${timestamp}`,
        customsCooClientRootPath: cooClientRoot,
        agentConsignmentClientRootPath: acdClientRoot,
        canSubmitCustomsCoo: true,
        canSubmitAgentConsignment: true,
      }),
    });
    if (!response.ok) {
      throw new Error(`Single Window invoice-list station profile save failed with HTTP ${response.status}: ${await response.text()}`);
    }
    const payload = await response.json();
    const activeProfile = Array.isArray(payload?.profiles)
      ? payload.profiles.find((profile) => profile?.isActive)
      : null;
    if (!activeProfile ||
        activeProfile.companyScope !== companyScope ||
        normalizePathForCompare(activeProfile.customsCooClientRootPath) !== normalizePathForCompare(cooClientRoot) ||
        normalizePathForCompare(activeProfile.agentConsignmentClientRootPath) !== normalizePathForCompare(acdClientRoot)) {
      throw new Error(`Single Window invoice-list station profile response is incomplete: ${JSON.stringify(payload)}`);
    }
    return activeProfile;
  }

  async function setInvoiceListStationAssignmentCode(page, stationAssignmentCode, timeoutMs) {
    await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const textarea = panel?.querySelector('.single-window-assignment-code-field textarea');
        const valueSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
        if (!textarea || !valueSetter || textarea.disabled) {
          throw new Error('Single Window station assignment code field is not available.');
        }

        valueSetter.call(textarea, ${JSON.stringify(stationAssignmentCode)});
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
        return textarea.value;
      })()`,
      true,
    );

    await waitForPageExpression(
      page,
      `document.querySelector('[aria-label="单一窗口办理"] .single-window-assignment-code-field textarea')?.value === ${JSON.stringify(stationAssignmentCode)}`,
      timeoutMs,
      "Timed out waiting for the invoice-list station assignment code to update.",
    );
  }
  
  async function runInvoiceListSingleWindowSubmitPackageCheck(
    page,
    options,
    accessToken,
    tokenType,
    invoice,
    businessType,
    buttonText,
    packagePath,
    timeoutMs,
  ) {
    await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const button = panel ? Array.from(panel.querySelectorAll('button'))
          .find((element) => (element.innerText || '').includes(${JSON.stringify(buttonText)})) : null;
        return Boolean(button && !button.disabled);
      })()`,
      timeoutMs,
      `Timed out waiting for invoice list Single Window submit action to become available: ${businessType}.`,
    );

    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeSavePackagePath = ${JSON.stringify(packagePath)};
        window.__exportDocManagerSmokeTauriInvocations = [];
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const button = panel ? Array.from(panel.querySelectorAll('button'))
          .find((element) => (element.innerText || '').includes(${JSON.stringify(buttonText)})) : null;
        if (!button || button.disabled) {
          throw new Error('Single Window submit package button is not available: ' + ${JSON.stringify(buttonText)});
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    let lastUiState = null;
    await waitFor(async () => {
      if (existsSync(packagePath)) {
        return true;
      }
  
      const state = await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="单一窗口办理"]');
          const text = panel ? panel.innerText || '' : '';
          const textarea = panel?.querySelector('.single-window-assignment-code-field textarea');
          const button = panel ? Array.from(panel.querySelectorAll('button'))
            .find((element) => (element.innerText || '').includes(${JSON.stringify(buttonText)})) : null;
          return {
            needsSecondClick: text.includes('导出前预检完成') || text.includes('确认后可再次点击导出继续'),
            readyWithoutIssues: text.includes('导出前预检未发现问题'),
            textExcerpt: text.slice(0, 1200),
            assignmentCodeValue: textarea?.value || '',
            buttonDisabled: button?.disabled ?? null,
            tauriInvocations: Array.isArray(window.__exportDocManagerSmokeTauriInvocations)
              ? window.__exportDocManagerSmokeTauriInvocations.slice(-10)
              : [],
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      lastUiState = state.value;
  
      if (state.value?.needsSecondClick) {
        await evaluate(
          page,
          `(() => {
            window.__exportDocManagerSmokeSingleWindowSecondClicks = window.__exportDocManagerSmokeSingleWindowSecondClicks || {};
            const key = ${JSON.stringify(businessType)};
            if (window.__exportDocManagerSmokeSingleWindowSecondClicks[key]) {
              return false;
            }
  
            const panel = document.querySelector('[aria-label="单一窗口办理"]');
            const button = panel ? Array.from(panel.querySelectorAll('button'))
              .find((element) => (element.innerText || '').includes(${JSON.stringify(buttonText)})) : null;
            if (!button || button.disabled) {
              throw new Error('Single Window submit package second-click button is not available: ' + ${JSON.stringify(buttonText)});
            }
  
            window.__exportDocManagerSmokeSingleWindowSecondClicks[key] = true;
            button.click();
            return true;
          })()`,
          true,
        );
      }
  
      return existsSync(packagePath) ? true : null;
    }, timeoutMs, () => `Timed out waiting for invoice list Single Window package export: ${businessType}.\nLast UI state: ${JSON.stringify(lastUiState)}`);
  
    const invocation = await waitForTauriCommandInvocation(
      page,
      "select_save_package_path",
      packagePath,
      timeoutMs,
      `Timed out waiting for invoice list Single Window save dialog invocation: ${businessType}.`,
    );
    const packageFile = await waitForPackageFile(packagePath, timeoutMs, `invoice list Single Window ${businessType} submit package`);
    const row = await waitForSingleWindowOperationCenterRow(
      options,
      accessToken,
      tokenType,
      invoice.invoiceNo,
      businessType,
      timeoutMs,
    );
  
    return {
      businessType,
      batchId: row.batchId,
      batchReference: row.batchReference,
      submitPackagePath: packagePath,
      submitPackageHeader: packageFile.header,
      submitPackageSize: packageFile.size,
      saveDialogInvocation: Boolean(invocation?.found),
    };
  }
  
  async function runInvoiceListSingleWindowReceiptImportCheck(
    page,
    options,
    accessToken,
    tokenType,
    batchId,
    packagePath,
    expectedMessage,
    expectedStatus,
    timeoutMs,
  ) {
    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeSingleWindowPackagePath = ${JSON.stringify(packagePath)};
        window.__exportDocManagerSmokeTauriInvocations = [];
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const button = panel ? Array.from(panel.querySelectorAll('button'))
          .find((element) => (element.innerText || '').includes('导入回执包')) : null;
        if (!button || button.disabled) {
          throw new Error('Single Window receipt package import button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const invocation = await waitForTauriCommandInvocation(
      page,
      "select_single_window_package_file",
      packagePath,
      timeoutMs,
      "Timed out waiting for invoice list Single Window receipt package open dialog invocation.",
    );
    const importUi = await waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="单一窗口办理"]');
        const text = panel ? panel.innerText || '' : '';
        return Boolean(text.includes('单一窗口回执包') &&
          text.includes('新增回执'));
      })()`,
      timeoutMs,
      "Timed out waiting for invoice list Single Window receipt import UI result.",
    );
    const detail = await waitFor(async () => {
      const candidate = await getSingleWindowBatchDetail(options, accessToken, tokenType, batchId);
      const receiptRecords = Array.isArray(candidate.receiptRecords) ? candidate.receiptRecords : [];
      return candidate.status === expectedStatus &&
        receiptRecords.some((record) => String(record.receiptMessage || "").includes(expectedMessage))
        ? candidate
        : null;
    }, timeoutMs, "Timed out waiting for invoice list receipt package import to persist on operation-center detail.");
  
    return {
      receiptPackagePath: packagePath,
      receiptImportDialogInvocation: Boolean(invocation?.found),
      receiptImportUi: Boolean(importUi?.found),
      detailStatus: detail.status,
      detailReceiptRecordCount: Array.isArray(detail.receiptRecords) ? detail.receiptRecords.length : 0,
      detailReceiptMessages: Array.isArray(detail.receiptRecords)
        ? detail.receiptRecords.map((record) => record.receiptMessage).filter(Boolean)
        : [],
    };
  }
  
  async function exportSmokeSingleWindowReceiptPackage(
    options,
    accessToken,
    tokenType,
    businessType,
    invoiceNo,
    batchReference,
    packagePath,
    receiptFiles,
  ) {
    const response = await fetch(new URL("/api/single-window/receipts/save-package-to-path", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        businessType,
        invoiceNo,
        batchReference,
        packagePath,
        receiptFiles,
      }),
    });
  
    if (!response.ok) {
      throw new Error(`Single Window receipt package smoke export failed with HTTP ${response.status}: ${await response.text()}`);
    }
  
    const payload = await response.json();
    if (!payload?.success || !payload?.packagePath) {
      throw new Error(`Single Window receipt package export response did not include success/packagePath: ${JSON.stringify(payload)}`);
    }
  
    return payload;
  }

  async function waitForPackageFile(filePath, timeoutMs, label) {
    return waitFor(async () => {
      if (!existsSync(filePath)) {
        return null;
      }
  
      const size = await readFileSize(filePath);
      const header = size > 0 ? readFileSync(filePath).subarray(0, 2).toString("ascii") : "";
      return size > 0 ? { size, header } : null;
    }, timeoutMs, `Timed out waiting for ${label} file: ${filePath}`);
  }
  
  async function waitForJobSucceeded(options, accessToken, tokenType, jobId, timeoutMs, label) {
    let latestJob = null;
    return waitFor(async () => {
      const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!response.ok) {
        throw new Error(`GET ${label} job ${jobId} failed with HTTP ${response.status}: ${await response.text()}`);
      }
  
      latestJob = await response.json();
      const status = String(latestJob.status ?? "").toLowerCase();
      if (status === "succeeded") {
        return latestJob;
      }
  
      if (status === "failed" || status === "canceled") {
        throw new Error(`${label} job ${jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
      }
  
      return null;
    }, timeoutMs, () => `Timed out waiting for ${label} job to finish. Latest: ${JSON.stringify(latestJob)}`);
  }

  async function waitForSingleWindowOperationCenterRow(
    options,
    accessToken,
    tokenType,
    invoiceNo,
    businessType,
    timeoutMs,
  ) {
    return waitFor(async () => {
      const response = await fetch(
        new URL(
          `/api/single-window/operation-center?keyword=${encodeURIComponent(invoiceNo)}&businessType=${encodeURIComponent(businessType)}&pageNumber=1&pageSize=20`,
          ensureTrailingSlash(options.apiBaseUrl),
        ),
        { headers: authorizedHeaders(options, accessToken, tokenType) },
      );
      if (!response.ok) {
        throw new Error(`Single Window operation center list failed with HTTP ${response.status}: ${await response.text()}`);
      }
  
      const page = await response.json();
      const rows = Array.isArray(page.rows) ? page.rows : [];
      return rows.find((row) =>
        String(row.invoiceNo || "") === String(invoiceNo || "") &&
        String(row.businessType || "") === String(businessType || "") &&
        Number(row.batchId || 0) > 0 &&
        String(row.batchReference || "").trim().length > 0) || null;
    }, timeoutMs, `Timed out waiting for Single Window operation center row: ${invoiceNo} / ${businessType}.`);
  }

  return { run: waitForInvoiceListDesktopWorkflowCheck };
}
