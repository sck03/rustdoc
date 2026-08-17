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
import { createSmokeStageRunner } from "./lib/web-runtime-smoke-deadline.mjs";
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
  delay,
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
  desktopAccessHeaders,
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
  const runStage = createSmokeStageRunner(options.globalTimeoutMs, options.timeoutMs);

  mkdirSync(options.userDataDir, { recursive: true });
  if (options.screenshotPath) {
    mkdirSync(path.dirname(options.screenshotPath), { recursive: true });
  }

  const login = await runStage("API login", () => loginToApi(options));
  const session = {
    accessToken: login.accessToken,
    expiresAt: login.expiresAt,
    apiBaseUrl: options.apiBaseUrl,
    user: login.user,
  };

  const chrome = await runStage("Chrome startup", (timeoutMs) => startChrome({ ...options, timeoutMs }));
  let cdp;
  let text = "";
  try {
    cdp = await runStage("DevTools connection", () => CdpClient.connect(chrome.browserWebSocketUrl));
    const page = await runStage("Browser session initialization", async () => {
      const value = await createPageSession(cdp);
      await injectDesktopSession(value, JSON.stringify(session), options, sessionStorageKey);
      await value.send("Page.navigate", { url: options.webUrl });
      return value;
    });

    text = await runStage("Runtime diagnostics", (timeoutMs) =>
      waitForRuntimeDiagnostics(page, options.expectedText, timeoutMs));
    const initialDiagnosticsText = text;
    const runtimePathActionsCheck = await runStage("Runtime path actions", (timeoutMs) =>
      waitForRuntimePathActionsCheck(page, options, timeoutMs));
    const runtimeDependencyClassification = await runStage("Runtime dependency classification", (timeoutMs) =>
      waitForRuntimeDependencyClassification(page, options, timeoutMs));
    const templateStorageCheck = await runStage("Template storage", (timeoutMs) =>
      waitForTemplateStorageCheck(page, options, timeoutMs));
    const frameDiagnostics = await runStage("Report frame diagnostics", (timeoutMs) => waitForFrameDiagnostics(
      page,
      options,
      timeoutMs,
      reportTemplateSmokeScene.readPageTemplateDiagnostics,
    ));
    const reportTemplateChecks = await runStage("Report templates", (timeoutMs) =>
      reportTemplateSmokeScene.run(page, options, timeoutMs));
    const invoiceReportCheck = await runStage("Invoice reports", (timeoutMs) => invoiceReportSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const invoiceItemsCheck = await runStage("Invoice items", (timeoutMs) => waitForInvoiceItemsCheck(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const invoiceLetterOfCreditCheck = await runStage("Letter-of-credit tools", (timeoutMs) => invoiceLetterOfCreditSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const invoiceDeleteCheck = await runStage("Invoice deletion", (timeoutMs) => invoiceQuerySmokeScene.runDelete(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const invoiceListDesktopWorkflowCheck = await runStage("Invoice desktop workflow", (timeoutMs) => invoiceListDesktopSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const queryKeyboardCheck = await runStage("Invoice query keyboard workflow", (timeoutMs) => invoiceQuerySmokeScene.runQuery(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const singleWindowEditorToolsCheck = await runStage("Single Window editor tools", (timeoutMs) => singleWindowEditorToolsSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const singleWindowOperationCenterCheck = await runStage("Single Window operation center", (timeoutMs) => singleWindowOperationCenterSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const {
      paymentReportCheck,
      paymentDeleteCheck,
    } = await runStage("Payment workflows", (timeoutMs) => paymentSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const masterDataDeleteCheck = await runStage("Master data workflows", (timeoutMs) => masterDataSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const jobCenterCheck = await runStage("Job center", (timeoutMs) => jobCenterSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const dashboardCheck = await runStage("Dashboard", (timeoutMs) =>
      waitForDashboardCheck(page, options, timeoutMs));
    const salesWorkspaceCheck = await runStage("Sales workspace", (timeoutMs) =>
      salesWorkspaceSmokeScene.run(page, options, timeoutMs));
    const {
      backupCheck,
      backupCreateCheck,
    } = await runStage("Backup preparation", (timeoutMs) =>
      settingsBackupSmokeScene.runPreparation(page, options, timeoutMs));
    const {
      updateCheck,
      smartOcrCheck,
      exchangeRateCheck,
      emailCheck,
      auditLogCheck,
      licenseCheck,
    } = await runStage("System tools", (timeoutMs) => systemToolsSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const {
      userManagementCrudCheck,
      userRows,
    } = await runStage("User management", (timeoutMs) => userManagementSmokeScene.run(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    if (options.expectedUserRows.length > 0) {
      const refreshedText = await runStage("User list refresh", () =>
        evaluate(page, "document.body ? document.body.innerText : ''", true)
          .catch(() => ({ value: text })));
      text = refreshedText.value ?? text;
    }
    const backupRestoreCheck = await runStage("Backup restore", (timeoutMs) => settingsBackupSmokeScene.runRestore(
      page,
      options,
      login.accessToken,
      login.tokenType,
      timeoutMs,
    ));
    const { title, href } = await runStage("Final browser state", async () => ({
      title: await evaluate(page, "document.title", true),
      href: await evaluate(page, "window.location.href", true),
    }));

    if (options.screenshotPath) {
      await runStage("Screenshot", () => captureScreenshot(page, options.screenshotPath));
    }

    await runStage("API logout", () => logoutFromApi(options, login.accessToken, login.tokenType));

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
    "有效订单共",
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
