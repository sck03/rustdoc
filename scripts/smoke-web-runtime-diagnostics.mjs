#!/usr/bin/env node
import { existsSync, mkdirSync, readFileSync, rmSync } from "node:fs";
import path from "node:path";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { createInvoiceDocumentOutputSmokeScene } from "./lib/web-runtime-invoice-document-output-scene.mjs";
import { createInvoiceItemTableSmokeScene } from "./lib/web-runtime-invoice-item-table-scene.mjs";
import { createInvoiceLetterOfCreditSmokeScene } from "./lib/web-runtime-invoice-letter-of-credit-scene.mjs";
import { createInvoiceListDesktopSmokeScene } from "./lib/web-runtime-invoice-list-desktop-scene.mjs";
import { createInvoiceQuerySmokeScene } from "./lib/web-runtime-invoice-query-scene.mjs";
import { createInvoiceReportSmokeScene } from "./lib/web-runtime-invoice-report-scene.mjs";
import { createInvoiceShippingMarkSmokeScene } from "./lib/web-runtime-invoice-shipping-mark-scene.mjs";
import { createJobCenterSmokeScene } from "./lib/web-runtime-job-center-scene.mjs";
import { createMasterDataSmokeScene } from "./lib/web-runtime-master-data-scene.mjs";
import { parseWebRuntimeSmokeArgs, validateWebRuntimeSmokeOptions } from "./lib/web-runtime-smoke-options.mjs";
import { createPaymentSmokeScene } from "./lib/web-runtime-payment-scene.mjs";
import { createReportTemplateSmokeScene } from "./lib/web-runtime-report-template-scene.mjs";
import { createSettingsBackupSmokeScene } from "./lib/web-runtime-settings-backup-scene.mjs";
import { createSingleWindowEditorToolsSmokeScene } from "./lib/web-runtime-single-window-editor-tools-scene.mjs";
import { createSingleWindowOperationCenterSmokeScene } from "./lib/web-runtime-single-window-operation-center-scene.mjs";
import { createSystemToolsSmokeScene } from "./lib/web-runtime-system-tools-scene.mjs";
import { createUserManagementSmokeScene } from "./lib/web-runtime-user-management-scene.mjs";
import { createSalesWorkspaceSmokeScene } from "./lib/web-runtime-sales-workspace-scene.mjs";
import {
  captureScreenshot,
  createPageSession,
  evaluate,
  startChrome,
} from "./lib/web-runtime-browser-session.mjs";
import { injectDesktopSession } from "./lib/web-runtime-desktop-session.mjs";
import {
  createSmokeInvoice,
  createSmokeProduct,
  deleteSmokeInvoice,
  deleteSmokeProduct,
  getApiSettings,
  getReportTemplates,
  saveApiSettings,
} from "./lib/web-runtime-api-fixtures.mjs";
import {
  buildSmokeAgentConsignmentReceiptXml,
  buildSmokeCustomsCooReceiptXml,
  getSingleWindowBatchDetail,
} from "./lib/web-runtime-single-window-fixtures.mjs";
import {
  waitForFrameDiagnostics,
  waitForPageExpression,
  waitForRuntimeDependencyClassification,
  waitForRuntimeDiagnostics,
  waitForRuntimePathActionsCheck,
  waitForTemplateStorageCheck,
  waitForTauriCommandInvocation,
} from "./lib/web-runtime-page-diagnostics.mjs";
import { loginToApi, logoutFromApi } from "./lib/web-runtime-auth-fixtures.mjs";
import { buildDashboardCheckUrl, buildInvoiceReportCheckUrl } from "./lib/web-runtime-navigation.mjs";
import {
  authorizedHeaders,
  authorizedJsonHeaders,
  buildBatchExportSettingsDeepLinkUrl,
  buildDocumentEmailSettingsDeepLinkUrl,
  buildSettingsSectionUrl,
  cleanupSmokeDirectory,
  cleanupSmokeFile,
  cloneJson,
  collectFilesByExtension,
  desktopAccessHeaders,
  ensureTrailingSlash,
  fetchJson,
  includesText,
  isPathInsideRoot,
  normalizePathForCompare,
  readFileSize,
  redactDesktopAccessToken,
  setRecordValueKeepingExistingCase,
  smokeFileNameFromPath,
  waitFor,
} from "./lib/web-runtime-smoke-common.mjs";

const sessionStorageKey = "exportdocmanager.web.session";
const systemToolsSmokeScene = createSystemToolsSmokeScene({
  authorizedHeaders,
  authorizedJsonHeaders,
  cleanupSmokeDirectory,
  cleanupSmokeFile,
  ensureTrailingSlash,
  evaluate,
  fetchJson,
  includesText,
  readFileSize,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const invoiceDocumentOutputSmokeScene = createInvoiceDocumentOutputSmokeScene({
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
});
const invoiceItemTableSmokeScene = createInvoiceItemTableSmokeScene({
  evaluate,
  waitFor,
  waitForPageExpression,
});
const invoiceLetterOfCreditSmokeScene = createInvoiceLetterOfCreditSmokeScene({
  createSmokeInvoice,
  deleteSmokeInvoice,
  evaluate,
  includesText,
  redactDesktopAccessToken,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const invoiceListDesktopSmokeScene = createInvoiceListDesktopSmokeScene({
  authorizedHeaders,
  authorizedJsonHeaders,
  buildSmokeAgentConsignmentReceiptXml,
  buildSmokeCustomsCooReceiptXml,
  createSmokeInvoice,
  deleteSmokeInvoice,
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
});
const invoiceQuerySmokeScene = createInvoiceQuerySmokeScene({
  authorizedHeaders,
  createSmokeInvoice,
  deleteSmokeInvoice,
  dispatchActiveElementKey,
  ensureTrailingSlash,
  evaluate,
  includesText,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const invoiceShippingMarkSmokeScene = createInvoiceShippingMarkSmokeScene({
  authorizedHeaders,
  ensureTrailingSlash,
  evaluate,
  waitFor,
});
const invoiceReportSmokeScene = createInvoiceReportSmokeScene({
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
});

const jobCenterSmokeScene = createJobCenterSmokeScene({
  authorizedHeaders,
  authorizedJsonHeaders,
  cleanupSmokeFile,
  createSmokeInvoice,
  deleteSmokeInvoice,
  ensureTrailingSlash,
  evaluate,
  includesText,
  normalizePathForCompare,
  readFileSize,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const masterDataSmokeScene = createMasterDataSmokeScene({
  authorizedHeaders,
  authorizedJsonHeaders,
  ensureTrailingSlash,
  evaluate,
  fetchJson,
  includesText,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const paymentSmokeScene = createPaymentSmokeScene({
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
});
const reportTemplateSmokeScene = createReportTemplateSmokeScene({
  evaluate,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const settingsBackupSmokeScene = createSettingsBackupSmokeScene({
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
});
const singleWindowEditorToolsSmokeScene = createSingleWindowEditorToolsSmokeScene({
  createSmokeInvoice,
  deleteSmokeInvoice,
  evaluate,
  includesText,
  normalizePathForCompare,
  redactDesktopAccessToken,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const singleWindowOperationCenterSmokeScene = createSingleWindowOperationCenterSmokeScene({
  authorizedHeaders,
  authorizedJsonHeaders,
  buildSmokeAgentConsignmentReceiptXml,
  buildSmokeCustomsCooReceiptXml,
  createSmokeInvoice,
  deleteSmokeInvoice,
  ensureTrailingSlash,
  evaluate,
  getSingleWindowBatchDetail,
  normalizePathForCompare,
  redactDesktopAccessToken,
  tryRemoveDirectory,
  waitFor,
  waitForPageExpression,
  waitForRuntimeDiagnostics,
});
const userManagementSmokeScene = createUserManagementSmokeScene({
  authorizedHeaders,
  ensureTrailingSlash,
  evaluate,
  includesText,
  waitFor,
  waitForPageExpression,
});
const salesWorkspaceSmokeScene = createSalesWorkspaceSmokeScene({
  evaluate,
  includesText,
  waitFor,
});

async function main() {
  const options = parseWebRuntimeSmokeArgs(process.argv.slice(2));
  validateWebRuntimeSmokeOptions(options);

  mkdirSync(options.userDataDir, { recursive: true });
  if (options.screenshotPath) {
    mkdirSync(path.dirname(options.screenshotPath), { recursive: true });
  }

  const login = await loginToApi(options);
  const session = {
    accessToken: login.accessToken,
    expiresAt: login.expiresAt,
    apiBaseUrl: options.apiBaseUrl,
    user: login.user,
  };

  const chrome = await startChrome(options);
  let cdp;
  let text = "";
  try {
    cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
    const page = await createPageSession(cdp);
    await injectDesktopSession(page, JSON.stringify(session), options, sessionStorageKey);
    await page.send("Page.navigate", { url: options.webUrl });

    text = await waitForRuntimeDiagnostics(page, options.expectedText, options.timeoutMs);
    const initialDiagnosticsText = text;
    const runtimePathActionsCheck = await waitForRuntimePathActionsCheck(page, options, options.timeoutMs);
    const runtimeDependencyClassification = await waitForRuntimeDependencyClassification(page, options, options.timeoutMs);
    const templateStorageCheck = await waitForTemplateStorageCheck(page, options, options.timeoutMs);
    const frameDiagnostics = await waitForFrameDiagnostics(
      page,
      options,
      options.timeoutMs,
      reportTemplateSmokeScene.readPageTemplateDiagnostics,
    );
    const reportTemplateChecks = await reportTemplateSmokeScene.run(page, options, options.timeoutMs);
    const invoiceReportCheck = await invoiceReportSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const invoiceItemsCheck = await waitForInvoiceItemsCheck(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const invoiceLetterOfCreditCheck = await invoiceLetterOfCreditSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const invoiceDeleteCheck = await invoiceQuerySmokeScene.runDelete(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const invoiceListDesktopWorkflowCheck = await invoiceListDesktopSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const queryKeyboardCheck = await invoiceQuerySmokeScene.runQuery(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const singleWindowEditorToolsCheck = await singleWindowEditorToolsSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const singleWindowOperationCenterCheck = await singleWindowOperationCenterSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const {
      paymentReportCheck,
      paymentDeleteCheck,
    } = await paymentSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const masterDataDeleteCheck = await masterDataSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const jobCenterCheck = await jobCenterSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const dashboardCheck = await waitForDashboardCheck(page, options, options.timeoutMs);
    const salesWorkspaceCheck = await salesWorkspaceSmokeScene.run(page, options, options.timeoutMs);
    const {
      backupCheck,
      backupCreateCheck,
    } = await settingsBackupSmokeScene.runPreparation(page, options, options.timeoutMs);
    const {
      updateCheck,
      smartOcrCheck,
      exchangeRateCheck,
      emailCheck,
      auditLogCheck,
      licenseCheck,
    } = await systemToolsSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const {
      userManagementCrudCheck,
      userRows,
    } = await userManagementSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    if (options.expectedUserRows.length > 0) {
      const refreshedText = await evaluate(page, "document.body ? document.body.innerText : ''", true).catch(() => ({ value: text }));
      text = refreshedText.value ?? text;
    }
    const backupRestoreCheck = await settingsBackupSmokeScene.runRestore(
      page,
      options,
      login.accessToken,
      login.tokenType,
      options.timeoutMs,
    );
    const title = await evaluate(page, "document.title", true);
    const href = await evaluate(page, "window.location.href", true);

    if (options.screenshotPath) {
      await captureScreenshot(page, options.screenshotPath);
    }

    await logoutFromApi(options, login.accessToken, login.tokenType);

    writeJson({
      success: true,
      webUrl: redactDesktopAccessToken(options.webUrl),
      apiBaseUrl: options.apiBaseUrl,
      desktopAccessTokenEnabled: Boolean(options.desktopAccessToken),
      mockTauriRuntimeContext: Boolean(options.mockTauriRuntimeContext),
      title: title.value ?? "",
      location: redactDesktopAccessToken(href.value ?? ""),
      checks: options.expectedText.map((value) => ({ value, found: includesText(initialDiagnosticsText, value) })),
      userRowChecks: options.expectedUserRows.map((expected) => ({
        ...expected,
        found: userRows.some((row) => includesText(row, expected.username) && includesText(row, expected.role)),
      })),
      frameDiagnostics,
      runtimePathActionsCheck,
      runtimeDependencyClassification,
      templateStorageCheck,
      reportTemplateChecks,
      invoiceReportCheck,
      invoiceItemsCheck,
      invoiceLetterOfCreditCheck,
      invoiceDeleteCheck,
      invoiceListDesktopWorkflowCheck,
      queryKeyboardCheck,
      singleWindowEditorToolsCheck,
      singleWindowOperationCenterCheck,
      paymentReportCheck,
      paymentDeleteCheck,
      masterDataDeleteCheck,
      jobCenterCheck,
      dashboardCheck,
      salesWorkspaceCheck,
      backupCheck,
      backupCreateCheck,
      updateCheck,
      smartOcrCheck,
      exchangeRateCheck,
      emailCheck,
      auditLogCheck,
      licenseCheck,
      userManagementCrudCheck,
      backupRestoreCheck,
      screenshotPath: options.screenshotPath || null,
      browserExecutable: options.browserExecutable,
      userDataDir: options.userDataDir,
      userRows,
      textExcerpt: text.slice(0, 1200),
    });
  } finally {
    cdp?.close();
    await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  }
}

async function waitForInvoiceItemsCheck(page, options, accessToken, tokenType, timeoutMs) {
  if (!options.invoiceItemsCheck) {
    return null;
  }

  const invoice = await createSmokeInvoice(options, accessToken, tokenType);
  const product = await createSmokeProduct(options, accessToken, tokenType);
  let result = null;
  let deletedInvoice = false;
  let deletedProduct = false;

  try {
    const checkUrl = buildInvoiceReportCheckUrl(options.webUrl, invoice.id);
    await page.send("Page.navigate", { url: checkUrl });
    await waitForRuntimeDiagnostics(page, ["发票编辑", "商品明细", invoice.invoiceNo], timeoutMs);

    const shortcutGuideCheck = await waitForPageExpression(
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
      autocompleteCheck,
      cellSelectionCheck,
      columnVisibilityCheck,
      keyboardNavigationCheck,
      productLibraryCheck,
      undoRedoCheck,
      workbenchModeCheck,
    } = await invoiceItemTableSmokeScene.run(page, product, timeoutMs);

    result = {
      invoiceId: invoice.id,
      invoiceNo: invoice.invoiceNo,
      shortcutGuideCheck,
      cellSelectionCheck,
      columnVisibilityCheck,
      workbenchModeCheck,
      productLibraryCheck,
      undoRedoCheck,
      autocompleteCheck,
      keyboardNavigationCheck,
      deletedInvoice,
      deletedProduct,
    };
  } finally {
    deletedProduct = await deleteSmokeProduct(options, accessToken, tokenType, product.id).catch(() => false);
    deletedInvoice = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
    if (result) {
      result.deletedInvoice = deletedInvoice;
      result.deletedProduct = deletedProduct;
    }
  }

  return result;
}




async function dispatchActiveElementKey(page, key, options = {}) {
  const shiftKey = Boolean(options.shiftKey);
  await evaluate(
    page,
    `(() => {
      const active = document.activeElement;
      if (!active) {
        throw new Error('No active element is available for key dispatch.');
      }

      active.dispatchEvent(new KeyboardEvent('keydown', {
        key: ${JSON.stringify(key)},
        shiftKey: ${JSON.stringify(shiftKey)},
        bubbles: true,
        cancelable: true,
      }));
      return true;
    })()`,
    true,
  );
}


function tryRemoveDirectory(directoryPath) {
  if (!directoryPath) {
    return false;
  }

  try {
    rmSync(directoryPath, { recursive: true, force: true });
    return !existsSync(directoryPath);
  } catch {
    return false;
  }
}

async function waitForDashboardCheck(page, options, timeoutMs) {
  if (!options.dashboardCheck) {
    return null;
  }

  const checkUrl = buildDashboardCheckUrl(options.webUrl);
  await page.send("Page.navigate", { url: checkUrl });
  const expectedText = [
    "仪表盘",
    "本月出口额",
    "本月预估利润",
    "本月退税额",
    "待处理订单",
    "已出运",
    "总订单量",
    "最新订单",
    "待办事项",
  ];

  const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
  const dashboardPageCheck = await waitForPageExpression(
    page,
    `(() => {
      const page = document.querySelector('[aria-label="仪表盘"]');
      return Boolean(page &&
        page.querySelector('.dashboard-metric-grid') &&
        page.querySelector('[aria-label="最新订单"] .dashboard-recent-table') &&
        page.querySelector('[aria-label="待办事项"] .dashboard-todo-list') &&
        Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('刷新仪表盘')));
    })()`,
    timeoutMs,
    "Timed out waiting for the dashboard page.",
  );

  return {
    url: redactDesktopAccessToken(checkUrl),
    expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
    dashboardPageCheck,
    textExcerpt: pageText.slice(0, 1200),
  };
}

function writeJson(value) {
  process.stdout.write(`${JSON.stringify(value, null, 2)}\n`);
}

main().catch((error) => {
  console.error(error.stack || error.message || String(error));
  process.exitCode = 1;
});
