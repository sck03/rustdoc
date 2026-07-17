import { existsSync } from "node:fs";

export function parseWebRuntimeSmokeArgs(args) {
  const options = {
    expectedText: [],
    expectedFrameText: [],
    expectedFrameSelectors: [],
    expectedFrameExpressions: [],
    reportTemplateChecks: [],
    expectedUserRows: [],
    expectedOpenPaths: [],
    runtimePathActionsCheck: false,
    userManagementCrudCheck: false,
    invoiceReportCheck: false,
    invoiceItemsCheck: false,
    invoiceLetterOfCreditCheck: false,
    invoiceDeleteCheck: false,
    invoiceListDesktopWorkflowCheck: false,
    queryKeyboardCheck: false,
    singleWindowEditorToolsCheck: false,
    singleWindowOperationCenterCheck: false,
    paymentReportCheck: false,
    paymentDeleteCheck: false,
    masterDataDeleteCheck: false,
    jobCenterCheck: false,
    dashboardCheck: false,
    salesWorkspaceCheck: false,
    backupCheck: false,
    backupCreateCheck: false,
    backupRestoreCheck: false,
    updateCheck: false,
    updateStageCheck: false,
    updateMandatoryCheck: false,
    smartOcrCheck: false,
    smartOcrRealSampleCheck: false,
    exchangeRateCheck: false,
    emailCheck: false,
    auditLogCheck: false,
    auditLogExportCheck: false,
    licenseCheck: false,
    timeoutMs: 45000,
  };

  for (let index = 0; index < args.length; index += 1) {
    const name = args[index];
    const nextValue = () => {
      index += 1;
      if (index >= args.length) {
        throw new Error(`Missing value for ${name}.`);
      }

      return args[index];
    };

    switch (name) {
      case "--browser-executable":
        options.browserExecutable = nextValue();
        break;
      case "--web-url":
        options.webUrl = nextValue();
        break;
      case "--api-base-url":
        options.apiBaseUrl = nextValue();
        break;
      case "--desktop-access-token":
        options.desktopAccessToken = nextValue();
        break;
      case "--mock-tauri-runtime-context":
        options.mockTauriRuntimeContext = true;
        break;
      case "--username":
        options.username = nextValue();
        break;
      case "--password":
        options.password = nextValue();
        break;
      case "--user-data-dir":
        options.userDataDir = nextValue();
        break;
      case "--timeout-ms":
        options.timeoutMs = Number(nextValue());
        break;
      case "--expected-text":
        options.expectedText.push(nextValue());
        break;
      case "--expected-frame-url":
        options.expectedFrameUrl = nextValue();
        break;
      case "--expected-frame-text":
        options.expectedFrameText.push(nextValue());
        break;
      case "--expected-frame-selector":
        options.expectedFrameSelectors.push(nextValue());
        break;
      case "--expected-frame-expression":
        options.expectedFrameExpressions.push(nextValue());
        break;
      case "--report-template-check": {
        const reportType = nextValue();
        const templateFileName = nextValue();
        const expectedFrameText = nextValue();
        options.reportTemplateChecks.push({ reportType, templateFileName, expectedFrameText });
        break;
      }
      case "--invoice-report-check":
        options.invoiceReportCheck = true;
        break;
      case "--invoice-items-check":
        options.invoiceItemsCheck = true;
        break;
      case "--invoice-letter-of-credit-check":
        options.invoiceLetterOfCreditCheck = true;
        break;
      case "--invoice-delete-check":
        options.invoiceDeleteCheck = true;
        break;
      case "--invoice-list-desktop-workflow-check":
        options.invoiceListDesktopWorkflowCheck = true;
        break;
      case "--query-keyboard-check":
        options.queryKeyboardCheck = true;
        break;
      case "--single-window-editor-tools-check":
        options.singleWindowEditorToolsCheck = true;
        break;
      case "--single-window-operation-center-check":
        options.singleWindowOperationCenterCheck = true;
        break;
      case "--payment-report-check":
        options.paymentReportCheck = true;
        break;
      case "--payment-delete-check":
        options.paymentDeleteCheck = true;
        break;
      case "--master-data-delete-check":
        options.masterDataDeleteCheck = true;
        break;
      case "--job-center-check":
        options.jobCenterCheck = true;
        break;
      case "--dashboard-check":
        options.dashboardCheck = true;
        break;
      case "--sales-workspace-check":
        options.salesWorkspaceCheck = true;
        break;
      case "--backup-check":
        options.backupCheck = true;
        break;
      case "--backup-create-check":
        options.backupCheck = true;
        options.backupCreateCheck = true;
        break;
      case "--backup-restore-check":
        options.backupCheck = true;
        options.backupRestoreCheck = true;
        break;
      case "--update-check":
        options.updateCheck = true;
        break;
      case "--update-stage-check":
        options.updateCheck = true;
        options.updateStageCheck = true;
        break;
      case "--update-mandatory-check":
        options.updateCheck = true;
        options.updateMandatoryCheck = true;
        break;
      case "--smart-ocr-check":
        options.smartOcrCheck = true;
        break;
      case "--smart-ocr-real-sample-check":
        options.smartOcrCheck = true;
        options.smartOcrRealSampleCheck = true;
        break;
      case "--exchange-rate-check":
        options.exchangeRateCheck = true;
        break;
      case "--email-check":
        options.emailCheck = true;
        break;
      case "--audit-log-check":
        options.auditLogCheck = true;
        break;
      case "--audit-log-export-check":
        options.auditLogCheck = true;
        options.auditLogExportCheck = true;
        break;
      case "--license-check":
        options.licenseCheck = true;
        break;
      case "--expected-user-row": {
        const username = nextValue();
        const role = nextValue();
        options.expectedUserRows.push({ username, role });
        break;
      }
      case "--runtime-path-actions-check":
        options.runtimePathActionsCheck = true;
        break;
      case "--expected-open-path":
        options.expectedOpenPaths.push(nextValue());
        break;
      case "--user-management-crud-check":
        options.userManagementCrudCheck = true;
        break;
      case "--screenshot-path":
        options.screenshotPath = nextValue();
        break;
      default:
        throw new Error(`Unknown argument: ${name}`);
    }
  }

  return options;
}

export function validateWebRuntimeSmokeOptions(options) {
  for (const key of ["browserExecutable", "webUrl", "apiBaseUrl", "username", "userDataDir"]) {
    if (!options[key] || typeof options[key] !== "string") {
      throw new Error(`Missing required argument --${toKebabCase(key)}.`);
    }
  }

  if (!existsSync(options.browserExecutable)) {
    throw new Error(`Browser executable was not found: ${options.browserExecutable}`);
  }

  if (!Number.isFinite(options.timeoutMs) || options.timeoutMs <= 0) {
    throw new Error("--timeout-ms must be a positive number.");
  }

  if (options.expectedText.length === 0) {
    throw new Error("At least one --expected-text value is required.");
  }

  if (
    (options.expectedFrameText.length > 0 ||
      options.expectedFrameSelectors.length > 0 ||
      options.expectedFrameExpressions.length > 0) &&
    !options.expectedFrameUrl
  ) {
    throw new Error("--expected-frame-url is required when frame checks are configured.");
  }

  if (typeof WebSocket === "undefined") {
    throw new Error("This smoke script requires Node.js with a global WebSocket implementation.");
  }

  if (options.runtimePathActionsCheck && !options.mockTauriRuntimeContext) {
    throw new Error("--runtime-path-actions-check requires --mock-tauri-runtime-context so path buttons are verified without opening the OS shell.");
  }

  if (options.runtimePathActionsCheck && options.expectedOpenPaths.length === 0) {
    throw new Error("--runtime-path-actions-check requires at least one --expected-open-path value.");
  }

  if (options.auditLogExportCheck && !options.mockTauriRuntimeContext) {
    throw new Error("--audit-log-export-check requires --mock-tauri-runtime-context so the export file open action is verified without opening the OS shell.");
  }

  if (options.singleWindowOperationCenterCheck && !options.mockTauriRuntimeContext) {
    throw new Error("--single-window-operation-center-check requires --mock-tauri-runtime-context so the receipt package save path is provided without opening the OS shell.");
  }

  if (options.invoiceListDesktopWorkflowCheck && !options.mockTauriRuntimeContext) {
    throw new Error("--invoice-list-desktop-workflow-check requires --mock-tauri-runtime-context so list-level desktop file dialogs are verified without opening OS dialogs.");
  }

  options.password ??= "";
}

function toKebabCase(value) {
  return value.replace(/[A-Z]/g, (match) => `-${match.toLowerCase()}`);
}
