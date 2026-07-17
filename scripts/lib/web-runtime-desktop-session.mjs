import path from "node:path";

export async function injectDesktopSession(page, sessionJson, options, sessionStorageKey) {
  const mockDirectorySelectionPath = path.join(options.userDataDir, "DefaultExportDirectorySmoke");
  const source = buildDesktopSessionBootstrap({
    apiBaseUrl: options.apiBaseUrl,
    desktopAccessToken: options.desktopAccessToken ?? "",
    mockDirectorySelectionPath,
    mockTauriRuntimeContext: options.mockTauriRuntimeContext,
    sessionJson,
    sessionStorageKey,
  });

  await page.send("Page.addScriptToEvaluateOnNewDocument", { source });
}

export function buildDesktopSessionBootstrap(options) {
  return `
try {
  window.localStorage.removeItem(${JSON.stringify(options.sessionStorageKey)});
  window.sessionStorage.setItem(${JSON.stringify(options.sessionStorageKey)}, ${JSON.stringify(options.sessionJson)});
  ${options.mockTauriRuntimeContext ? buildMockTauriBootstrap(options) : ""}
} catch (error) {
  console.error("ExportDocManager smoke failed to inject session", error);
}
`;
}

function buildMockTauriBootstrap(options) {
  return `
  window.__TAURI__ = {
    core: {
      invoke: async (command, args = {}) => {
        window.__exportDocManagerSmokeTauriInvocations = window.__exportDocManagerSmokeTauriInvocations || [];
        const invocation = { command, args };
        window.__exportDocManagerSmokeTauriInvocations.push(invocation);
        if (command === "get_desktop_runtime_context") {
          return {
            apiBaseUrl: ${JSON.stringify(options.apiBaseUrl)},
            desktopAccessToken: ${JSON.stringify(options.desktopAccessToken)},
          };
        }

        if (command === "open_path") {
          return null;
        }

        if (command === "check_tauri_update") {
          const result = window.__exportDocManagerSmokeTauriUpdateResult || {
            supported: true,
            configured: true,
            updateAvailable: false,
            currentVersion: "0.1.0",
            latestVersion: "0.1.0",
            target: "",
            downloadUrl: "",
            body: "",
            date: "",
            statusText: "Tauri updater 检查完成，当前已是最新版本。",
            errorMessage: "",
            storagePolicy: "多平台安装由 Tauri updater 插件处理；业务数据库、授权文件和运行数据仍留在运行目录 App_Data。",
          };
          invocation.result = result;
          return result;
        }

        if (command === "install_tauri_update") {
          const update = window.__exportDocManagerSmokeTauriUpdateResult || {};
          const result = {
            success: Boolean(update.updateAvailable),
            installedVersion: update.latestVersion || "",
            statusText: update.updateAvailable ? "Tauri updater 已完成安装并请求重启。" : "Tauri updater 未发现可安装的新版本。",
            restartPolicy: "安装完成后由 Tauri 请求重启。",
            storagePolicy: "多平台安装由 Tauri updater 插件处理；业务数据库、授权文件和运行数据仍留在运行目录 App_Data。",
          };
          invocation.result = result;
          return result;
        }

        if (command === "request_app_exit" || command === "select_report_template_file") {
          return null;
        }

        if (command === "select_customs_coo_attachment_files") {
          const attachmentPath = window.__exportDocManagerSmokeCooAttachmentPath || "";
          return attachmentPath ? [attachmentPath] : [];
        }

        if (command === "select_directory") {
          return window.__exportDocManagerSmokeDirectoryPath || ${JSON.stringify(options.mockDirectorySelectionPath)};
        }

        if (command === "select_save_package_path") {
          return window.__exportDocManagerSmokeSavePackagePath || null;
        }

        if (command === "select_save_invoice_transfer_package_path") {
          return window.__exportDocManagerSmokeSaveInvoiceTransferPackagePath || null;
        }

        if (command === "select_invoice_transfer_package_file") {
          return window.__exportDocManagerSmokeInvoiceTransferPackagePath || null;
        }

        if (command === "select_save_excel_path") {
          return window.__exportDocManagerSmokeSaveExcelPath || null;
        }

        if (command === "select_single_window_package_file") {
          return window.__exportDocManagerSmokeSingleWindowPackagePath || null;
        }

        throw new Error("Unsupported mocked Tauri command: " + command);
      },
    },
  };
`;
}
