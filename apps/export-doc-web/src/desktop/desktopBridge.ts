import { invoke, isTauri } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export type DesktopRuntimeContext = {
  apiBaseUrl: string;
  desktopAccessToken: string;
  productEdition: "Document" | "Sales" | "Full";
  platform: string;
  singleWindowStationCapable: boolean;
};

export type RuntimeStorageContext = {
  dataRoot: string;
  portable: boolean;
  migrationSupported: boolean;
  storagePolicy: string;
};

export type DataRootMigrationScheduleResult = {
  currentDataRoot: string;
  targetDataRoot: string;
  restartRequired: boolean;
  message: string;
};

export type TauriUpdaterCheckResult = {
  supported: boolean;
  installSupported: boolean;
  configured: boolean;
  updateAvailable: boolean;
  currentVersion: string;
  latestVersion: string;
  target: string;
  downloadUrl: string;
  body: string;
  date: string;
  statusText: string;
  errorMessage: string;
  storagePolicy: string;
};

export type TauriUpdaterInstallResult = {
  success: boolean;
  installedVersion: string;
  statusText: string;
  restartPolicy: string;
  storagePolicy: string;
};

export type TauriUpdaterCancelResult = {
  accepted: boolean;
  statusText: string;
};

export type TauriUpdaterProgress = {
  phase: "preparing" | "downloading" | "verifying" | "installing" | "restarting" | "canceled";
  downloadedBytes: number;
  totalBytes?: number | null;
  progressPercent?: number | null;
  statusText: string;
};

function getInvoke() {
  return typeof window !== "undefined" && isTauri() ? invoke : undefined;
}

export function isDesktopBridgeAvailable() {
  return Boolean(getInvoke());
}

export async function getDesktopRuntimeContext() {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<DesktopRuntimeContext>("get_desktop_runtime_context");
}

export async function selectSingleWindowPackageFile() {
  return invokeOptionalPath("select_single_window_package_file");
}

export async function selectInvoiceTransferPackageFile() {
  return invokeOptionalPath("select_invoice_transfer_package_file");
}

export async function selectDisasterRecoveryPackageFile() {
  return invokeOptionalPath("select_disaster_recovery_package_file");
}

export async function selectReceiptFile() {
  return invokeOptionalPath("select_receipt_file");
}

export async function selectReceiptFiles() {
  const invoke = getInvoke();
  if (!invoke) {
    return [];
  }

  return invoke<string[]>("select_receipt_files");
}

export async function selectPdfFiles() {
  const invoke = getInvoke();
  if (!invoke) {
    return [];
  }

  return invoke<string[]>("select_pdf_files");
}

export async function selectEmailAttachmentFiles() {
  const invoke = getInvoke();
  if (!invoke) {
    return [];
  }

  return invoke<string[]>("select_email_attachment_files");
}

export async function selectCustomsCooAttachmentFiles() {
  const invoke = getInvoke();
  if (!invoke) {
    return [];
  }

  return invoke<string[]>("select_customs_coo_attachment_files");
}

export async function selectLetterOfCreditFile() {
  return invokeOptionalPath("select_letter_of_credit_file");
}

export async function selectOcrImageFile() {
  return invokeOptionalPath("select_ocr_image_file");
}

export async function selectExporterSealImageFile() {
  return invokeOptionalPath("select_exporter_seal_image_file");
}

export async function readExporterSealImageFileAsDataUrl(path: string) {
  const invoke = getInvoke();
  if (!invoke || !path.trim()) {
    return null;
  }

  return invoke<string>("read_exporter_seal_image_file_as_data_url", { path });
}

export async function selectExcelFile() {
  return invokeOptionalPath("select_excel_file");
}

export async function selectDirectory(defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_directory", {
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectReportTemplatePackageFile() {
  return invokeOptionalPath("select_report_template_package_file");
}

export async function selectSavePackagePath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_package_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectSaveInvoiceTransferPackagePath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_invoice_transfer_package_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectSaveReportTemplatePackagePath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_report_template_package_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectSavePdfPath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_pdf_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectSaveZipPath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_zip_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function selectSaveExcelPath(defaultFileName?: string, defaultDirectory?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>("select_save_excel_path", {
    defaultFileName: normalizeOptionalPath(defaultFileName),
    defaultDirectory: normalizeOptionalPath(defaultDirectory),
  });
}

export async function openPath(path: string) {
  const invoke = getInvoke();
  if (!invoke || !path.trim()) {
    return false;
  }

  await invoke<void>("open_path", { path });
  return true;
}

export async function getRuntimeStorageContext() {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<RuntimeStorageContext>("get_runtime_storage_context");
}

export async function scheduleDataRootMigration() {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<DataRootMigrationScheduleResult | null>("schedule_data_root_migration");
}

export async function logFrontendError(payload: {
  message: string;
  source?: string;
  stack?: string;
  url?: string;
}) {
  const invoke = getInvoke();
  if (!invoke) {
    return false;
  }

  await invoke<void>("log_frontend_error", {
    message: payload.message,
    source: payload.source || null,
    stack: payload.stack || null,
    url: payload.url || null,
  });
  return true;
}

export async function checkTauriUpdate(endpoint?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<TauriUpdaterCheckResult>("check_tauri_update", {
    endpoint: normalizeOptionalText(endpoint),
  });
}

export async function installTauriUpdate(endpoint?: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<TauriUpdaterInstallResult>("install_tauri_update", {
    endpoint: normalizeOptionalText(endpoint),
  });
}

export async function cancelTauriUpdate() {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<TauriUpdaterCancelResult>("cancel_tauri_update");
}

export async function subscribeToTauriUpdaterProgress(handler: (progress: TauriUpdaterProgress) => void) {
  if (!getInvoke()) {
    return () => undefined;
  }

  return listen<TauriUpdaterProgress>("exportdoc://updater-progress", (event) => handler(event.payload));
}

export async function requestAppExit() {
  const invoke = getInvoke();
  if (!invoke) {
    return false;
  }

  await invoke<void>("request_app_exit");
  return true;
}

export async function subscribeToAppExitRequests(handler: () => void | Promise<void>) {
  if (!getInvoke()) {
    return () => undefined;
  }

  return listen("exportdoc://exit-requested", () => {
    void handler();
  });
}

async function invokeOptionalPath(command: string) {
  const invoke = getInvoke();
  if (!invoke) {
    return null;
  }

  return invoke<string | null>(command);
}

function normalizeOptionalPath(value?: string) {
  return normalizeOptionalText(value);
}

function normalizeOptionalText(value?: string) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}
